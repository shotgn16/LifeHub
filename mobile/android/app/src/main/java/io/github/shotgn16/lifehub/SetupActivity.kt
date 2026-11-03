package io.github.shotgn16.lifehub

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
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

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_setup)
        prefs = Prefs(this)
        server = findViewById(R.id.server)
        key = findViewById(R.id.key)
        status = findViewById(R.id.status)
        notify = findViewById(R.id.notify)

        server.setText(prefs.server)
        key.setText(prefs.apiKey)
        notify.isChecked = prefs.notifications
        if (prefs.personName.isNotEmpty()) status.text = "Signed in as ${prefs.personName}"

        findViewById<Button>(R.id.save).setOnClickListener { save() }
        findViewById<Button>(R.id.open).setOnClickListener { openLifeHub() }
        notify.setOnCheckedChangeListener { _, on ->
            prefs.notifications = on
            if (on && Build.VERSION.SDK_INT >= 33 &&
                checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
                requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 10)
            SyncWorker.schedule(this)
        }
    }

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
}
