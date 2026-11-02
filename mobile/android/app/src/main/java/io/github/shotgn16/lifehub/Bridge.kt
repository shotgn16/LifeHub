package io.github.shotgn16.lifehub

import android.content.Intent
import android.webkit.JavascriptInterface
import org.json.JSONObject
import kotlin.concurrent.thread

/** What the offline screen (and LifeHub's own pages) can ask the app to do. Every method takes and returns a string. */
class Bridge(private val activity: MainActivity) {
    private val store = OfflineStore(activity)

    @JavascriptInterface
    fun state(arg: String): String = store.state()

    /** Saves a change on the phone, then tries to send it straight away. */
    @JavascriptInterface
    fun queue(arg: String): String {
        val o = JSONObject(arg)
        store.enqueue(o.getString("type"), o.optJSONObject("data") ?: JSONObject())
        SyncWorker.runNow(activity)          // also sends it later if there's no connection right now
        thread { store.sync(); activity.runOnUiThread { activity.refreshOffline() } }
        return store.state()
    }

    @JavascriptInterface
    fun sync(arg: String): String {
        store.sync()
        return store.state()
    }

    @JavascriptInterface
    fun goOnline(arg: String): String {
        activity.runOnUiThread { activity.goOnline() }
        return ""
    }

    @JavascriptInterface
    fun goOffline(arg: String): String {
        activity.runOnUiThread { activity.showOffline() }
        return ""
    }

    @JavascriptInterface
    fun settings(arg: String): String {
        activity.runOnUiThread { activity.startActivity(Intent(activity, SetupActivity::class.java)) }
        return ""
    }
}
