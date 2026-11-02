package io.github.shotgn16.lifehub

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
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

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        if (!prefs.configured) {
            startActivity(Intent(this, SetupActivity::class.java))
            finish()
            return
        }
        web = WebView(this)
        setContentView(web)
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
                // Lets the web app show phone-only links such as "Phone app settings".
                view.evaluateJavascript("document.documentElement.classList.add('in-app')", null)
            }

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (!request.isForMainFrame) return
                AlertDialog.Builder(this@MainActivity)
                    .setTitle("Can't reach LifeHub")
                    .setMessage("${prefs.server}\n\n${error.description}\n\nAre you on your home network (or is your LifeHub address reachable from outside)?")
                    .setPositiveButton("Try again") { _, _ -> view.reload() }
                    .setNegativeButton("Settings") { _, _ -> startActivity(Intent(this@MainActivity, SetupActivity::class.java)) }
                    .show()
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
        if (::web.isInitialized && web.canGoBack()) web.goBack() else @Suppress("DEPRECATION") super.onBackPressed()
    }

    override fun onResume() {
        super.onResume()
        if (::web.isInitialized) web.onResume()
    }

    override fun onPause() {
        if (::web.isInitialized) web.onPause()
        CookieManager.getInstance().flush()
        super.onPause()
    }

    companion object {
        private const val REQ_CAMERA = 1
        private const val REQ_FILE = 2
    }
}

object BuildConfigInfo {
    fun version(activity: Activity): String = try {
        activity.packageManager.getPackageInfo(activity.packageName, 0).versionName ?: "1"
    } catch (_: Exception) { "1" }
}
