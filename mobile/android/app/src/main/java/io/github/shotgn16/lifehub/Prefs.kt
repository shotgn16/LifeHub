package io.github.shotgn16.lifehub

import android.content.Context
import android.content.SharedPreferences

/** Where your LifeHub is and how this phone signs in. Stored privately on the phone. */
class Prefs(context: Context) {
    private val p: SharedPreferences = context.getSharedPreferences("lifehub", Context.MODE_PRIVATE)

    var server: String
        get() = p.getString("server", "") ?: ""
        set(v) = p.edit().putString("server", v.trim().trimEnd('/')).apply()
    var apiKey: String
        get() = p.getString("apiKey", "") ?: ""
        set(v) = p.edit().putString("apiKey", v.trim()).apply()
    var personName: String
        get() = p.getString("personName", "") ?: ""
        set(v) = p.edit().putString("personName", v).apply()
    var notifications: Boolean
        get() = p.getBoolean("notifications", true)
        set(v) = p.edit().putBoolean("notifications", v).apply()
    /** Newest notification already shown (ISO time from the server). */
    var lastNotice: String
        get() = p.getString("lastNotice", "") ?: ""
        set(v) = p.edit().putString("lastNotice", v).apply()
    var lastSync: String
        get() = p.getString("lastSync", "") ?: ""
        set(v) = p.edit().putString("lastSync", v).apply()

    val configured: Boolean get() = server.startsWith("http") && apiKey.isNotEmpty()
}
