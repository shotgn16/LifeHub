package io.github.shotgn16.lifehub

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.webkit.CookieManager
import android.webkit.PermissionRequest
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import java.net.URLEncoder

/**
 * LifeHub in a full-screen web view, signed in with your API key. Adds what a browser can't:
 * the share menu, home-screen shortcuts, background notifications and Bible-app timing.
 */
class MainActivity : Activity() {
    private lateinit var prefs: Prefs
    private lateinit var web: WebView
    private var fileCallback: ValueCallback<Array<Uri>>? = null
    private var pendingPermission: PermissionRequest? = null
    private var signedIn = false
    private var showingOffline = false
    private lateinit var store: OfflineStore

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        if (!prefs.configured) {
            startActivity(Intent(this, SetupActivity::class.java))
            finish()
            return
        }
        store = OfflineStore(this)
        web = WebView(this)
        setContentView(web)
        web.addJavascriptInterface(Bridge(this), "LifeHubNative")
        CookieManager.getInstance().setAcceptCookie(true)
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false)
        web.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            mediaPlaybackRequiresUserGesture = false
            userAgentString = "$userAgentString LifeHubApp/${BuildConfigInfo.version(this@MainActivity)} Android"
        }
        web.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val url = request.url
                if (url.scheme == "lifehub-app") {
                    if (url.host == "settings") startActivity(Intent(this@MainActivity, SetupActivity::class.java))
                    return true
                }
                // Links to other sites (YouVersion, StudyLight, a store's offers) open in their own app or the browser.
                if (url.host != Uri.parse(prefs.server).host) {
                    try { startActivity(Intent(Intent.ACTION_VIEW, url)) } catch (_: Exception) { }
                    return true
                }
                return false
            }

            override fun onPageFinished(view: WebView, url: String) {
                if (url.startsWith("file:")) return
                // Phone-only links ("Phone app settings"), and switching to the offline screen if the connection drops.
                view.evaluateJavascript(WATCH_SCRIPT, null)
                // While online, keep the copy on the phone fresh (and send anything still waiting).
                Thread { store.sync(force = false) }.start()
            }

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (request.isForMainFrame) showOffline()
            }
        }
        web.webChromeClient = object : WebChromeClient() {
            override fun onPermissionRequest(request: PermissionRequest) {
                runOnUiThread {
                    if (PermissionRequest.RESOURCE_VIDEO_CAPTURE in request.resources) {
                        if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
                            request.grant(arrayOf(PermissionRequest.RESOURCE_VIDEO_CAPTURE))
                        else {
                            pendingPermission = request
                            requestPermissions(arrayOf(Manifest.permission.CAMERA), REQ_CAMERA)
                        }
                    } else request.deny()
                }
            }

            override fun onShowFileChooser(view: WebView, callback: ValueCallback<Array<Uri>>, params: FileChooserParams): Boolean {
                fileCallback?.onReceiveValue(null)
                fileCallback = callback
                return try {
                    startActivityForResult(params.createIntent(), REQ_FILE); true
                } catch (_: Exception) {
                    fileCallback = null; false
                }
            }
        }
        SyncWorker.schedule(this)
        SyncWorker.runNow(this)
        open(pathFrom(intent) ?: "", relogin = true)
    }

    /**
     * LifeHub can't be reached: show the offline screen with the copy saved on the phone. If it turns out
     * LifeHub *is* reachable (e.g. a stale connection after the phone slept), just reload it instead.
     */
    fun showOffline() {
        if (showingOffline) return
        showingOffline = true
        Thread {
            val ok = store.sync()
            runOnUiThread {
                if (ok && store.pending().length() == 0) { showingOffline = false; open("", relogin = true) }
                else web.loadUrl("file:///android_asset/offline.html")
            }
        }.start()
    }

    fun goOnline() {
        showingOffline = false
        open("", relogin = true)
    }

    fun refreshOffline() {
        if (showingOffline) web.evaluateJavascript("window.lifehubRefresh && window.lifehubRefresh()", null)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        val path = pathFrom(intent) ?: return
        open(path, relogin = intent.data?.getQueryParameter("relogin") == "1")
    }

    /** What to open: a shared passage or note, a shortcut, or a notification's link. */
    private fun pathFrom(intent: Intent?): String? {
        if (intent == null) return null
        if (intent.action == Intent.ACTION_SEND) {
            val text = listOfNotNull(intent.getStringExtra(Intent.EXTRA_SUBJECT), intent.getStringExtra(Intent.EXTRA_TEXT))
                .joinToString("\n").trim()
            if (text.isNotEmpty()) return "bible/share?text=" + URLEncoder.encode(text, "UTF-8")
        }
        val data = intent.data
        if (data?.scheme == "lifehub") return data.getQueryParameter("path") ?: ""
        return null
    }

    private fun open(path: String, relogin: Boolean) {
        showingOffline = false
        val clean = path.trimStart('/')
        if (!signedIn || relogin) {
            // Sign in with this person's API key, then go to the page.
            val body = "key=" + URLEncoder.encode(prefs.apiKey, "UTF-8") + "&returnUrl=" + URLEncoder.encode("/$clean", "UTF-8")
            web.postUrl(prefs.server + "/auth/app", body.toByteArray())
            signedIn = true
        } else web.loadUrl("${prefs.server}/$clean")
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        if (requestCode == REQ_FILE) {
            fileCallback?.onReceiveValue(WebChromeClient.FileChooserParams.parseResult(resultCode, data))
            fileCallback = null
        } else @Suppress("DEPRECATION") super.onActivityResult(requestCode, resultCode, data)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        if (requestCode == REQ_CAMERA) {
            val req = pendingPermission
            pendingPermission = null
            if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) req?.grant(arrayOf(PermissionRequest.RESOURCE_VIDEO_CAPTURE))
            else req?.deny()
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (::web.isInitialized && !showingOffline && web.canGoBack()) web.goBack() else @Suppress("DEPRECATION") super.onBackPressed()
    }

    override fun onResume() {
        super.onResume()
        if (::web.isInitialized) {
            web.onResume()
            // Back from the background while offline: see whether LifeHub is reachable now.
            if (showingOffline) Thread { store.sync(); runOnUiThread { refreshOffline() } }.start()
        }
    }

    override fun onPause() {
        if (::web.isInitialized) web.onPause()
        CookieManager.getInstance().flush()
        super.onPause()
    }

    companion object {
        private const val REQ_CAMERA = 1
        private const val REQ_FILE = 2

        /** Runs in LifeHub's pages: marks the app, and if the live connection stays down for 12 s, switches to offline. */
        private const val WATCH_SCRIPT = """
            (function () {
              document.documentElement.classList.add('in-app');
              if (window.__lifehubWatch) return; window.__lifehubWatch = 1;
              var since = 0;
              setInterval(function () {
                var m = document.getElementById('components-reconnect-modal');
                var shown = m && getComputedStyle(m).display !== 'none' && getComputedStyle(m).visibility !== 'hidden';
                if (!shown) { since = 0; return; }
                since = since || Date.now();
                if (Date.now() - since > 12000) { since = 0; try { LifeHubNative.goOffline(''); } catch (e) {} }
              }, 2000);
            })();
        """
    }
}

object BuildConfigInfo {
    fun version(activity: Activity): String = try {
        activity.packageManager.getPackageInfo(activity.packageName, 0).versionName ?: "1"
    } catch (_: Exception) { "1" }
}
