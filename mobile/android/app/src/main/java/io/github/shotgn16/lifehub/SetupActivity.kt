package io.github.shotgn16.lifehub

import android.Manifest
import android.app.Activity
import android.app.AppOpsManager
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Process
import android.provider.Settings
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.Switch
import android.widget.TextView
import kotlin.concurrent.thread

/** First run and "Phone app settings": your LifeHub address, your API key and the phone-only extras. */
class SetupActivity : Activity() {
    private lateinit var prefs: Prefs
    private lateinit var server: EditText
    private lateinit var key: EditText
    private lateinit var status: TextView
    private lateinit var notify: Switch
    private lateinit var bible: Switch
    private lateinit var bibleStatus: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_setup)
        prefs = Prefs(this)
        server = findViewById(R.id.server)
        key = findViewById(R.id.key)
        status = findViewById(R.id.status)
        notify = findViewById(R.id.notify)
        bible = findViewById(R.id.bible)
        bibleStatus = findViewById(R.id.bibleStatus)

        server.setText(prefs.server)
        key.setText(prefs.apiKey)
        notify.isChecked = prefs.notifications
        bible.isChecked = prefs.bibleTracking
        if (prefs.personName.isNotEmpty()) status.text = "Signed in as ${prefs.personName}"

        findViewById<Button>(R.id.save).setOnClickListener { save() }
        findViewById<Button>(R.id.open).setOnClickListener { openLifeHub() }
        findViewById<Button>(R.id.usageAccess).setOnClickListener {
            startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
        }
        notify.setOnCheckedChangeListener { _, on ->
            prefs.notifications = on
            if (on && Build.VERSION.SDK_INT >= 33 &&
                checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
                requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 10)
            SyncWorker.schedule(this)
        }
        bible.setOnCheckedChangeListener { _, on ->
            prefs.bibleTracking = on
            if (on && !hasUsageAccess()) startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
            refreshBibleStatus()
            SyncWorker.schedule(this)
        }
    }

    override fun onResume() {
        super.onResume()
        refreshBibleStatus()
    }

    private fun refreshBibleStatus() {
        bibleStatus.text = when {
            !prefs.bibleTracking -> "Off. LifeHub can time how long you spend in the YouVersion Bible app, straight from this phone — no Home Assistant needed."
            !hasUsageAccess() -> "Needs Usage access: tap the button below, find LifeHub and switch it on."
            else -> "On — time in the Bible app is sent to LifeHub every 15 minutes." +
                if (prefs.lastSync.isNotEmpty()) "\nLast sent: ${prefs.lastSync}" else ""
        }
        findViewById<View>(R.id.usageAccess).visibility = if (prefs.bibleTracking && !hasUsageAccess()) View.VISIBLE else View.GONE
    }

    fun hasUsageAccess(): Boolean = usageAccess(this)

    private fun save() {
        var url = server.text.toString().trim().trimEnd('/')
        if (url.isNotEmpty() && !url.startsWith("http")) url = "http://$url"
        val k = key.text.toString().trim()
        if (url.isEmpty() || k.isEmpty()) { status.text = "Enter your LifeHub address and your API key."; return }
        status.text = "Checking…"
        thread {
            try {
                val me = Api(url, k).me()
                runOnUiThread {
                    prefs.server = url
                    prefs.apiKey = k
                    prefs.personName = me.optString("name")
                    me.optString("bibleAppMatch").takeIf { it.contains('.') }?.let { prefs.bibleApp = it }
                    status.text = "Signed in as ${prefs.personName} · ${me.optString("household")} (LifeHub ${me.optString("version")})"
                    SyncWorker.schedule(this)
                    if (Build.VERSION.SDK_INT >= 33 && prefs.notifications &&
                        checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
                        requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 10)
                }
            } catch (e: Exception) {
                runOnUiThread { status.text = "Couldn't reach LifeHub: ${e.message ?: e.javaClass.simpleName}" }
            }
        }
    }

    private fun openLifeHub() {
        if (!prefs.configured) { status.text = "Save your LifeHub address and API key first."; return }
        startActivity(Intent(this, MainActivity::class.java).setData(Uri.parse("lifehub://open?path=&relogin=1")))
        finish()
    }

    companion object {
        fun usageAccess(activity: android.content.Context): Boolean {
            val ops = activity.getSystemService(APP_OPS_SERVICE) as AppOpsManager
            val mode = if (Build.VERSION.SDK_INT >= 29)
                ops.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), activity.packageName)
            else @Suppress("DEPRECATION") ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), activity.packageName)
            return mode == AppOpsManager.MODE_ALLOWED
        }
    }
}
