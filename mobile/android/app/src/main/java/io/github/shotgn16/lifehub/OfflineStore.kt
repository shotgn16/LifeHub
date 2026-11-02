package io.github.shotgn16.lifehub

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.util.Date
import java.util.UUID

/**
 * Keeps a copy of your LifeHub data on the phone for when it can't be reached, plus the changes you made
 * offline ("outbox"). [sync] sends the outbox to LifeHub (each change has its own id, so a retry is never
 * applied twice) and refreshes the saved copy.
 */
class OfflineStore(context: Context) {
    private val app = context.applicationContext
    private val snapshotFile = File(app.filesDir, "offline-snapshot.json")
    private val outboxFile = File(app.filesDir, "offline-outbox.json")

    companion object {
        private val lock = Any()
        @Volatile var online: Boolean = false
        @Volatile var lastError: String = ""
        @Volatile private var lastRefresh = 0L
    }

    fun snapshot(): JSONObject? = synchronized(lock) {
        try { if (snapshotFile.exists()) JSONObject(snapshotFile.readText()) else null } catch (_: Exception) { null }
    }

    fun pending(): JSONArray = synchronized(lock) {
        try { if (outboxFile.exists()) JSONArray(outboxFile.readText()) else JSONArray() } catch (_: Exception) { JSONArray() }
    }

    private fun writeAtomic(file: File, text: String) {
        val tmp = File(file.path + ".tmp")
        tmp.writeText(text)
        if (!tmp.renameTo(file)) { file.writeText(text); tmp.delete() }
    }

    fun enqueue(type: String, data: JSONObject) = synchronized(lock) {
        val list = pending()
        list.put(JSONObject().put("id", UUID.randomUUID().toString()).put("type", type)
            .put("at", SyncWorker.iso(Date())).put("data", data))
        writeAtomic(outboxFile, list.toString())
    }

    fun state(): String {
        val snap = snapshot()
        return JSONObject()
            .put("snapshot", snap ?: JSONObject.NULL)
            .put("pending", pending())
            .put("online", online)
            .put("lastError", lastError)
            .put("lastSyncUtc", snap?.optString("generatedUtc") ?: "")
            .toString()
    }

    /** Sends waiting changes and refreshes the saved copy. Returns true if LifeHub was reachable. */
    fun sync(force: Boolean = true): Boolean {
        val prefs = Prefs(app)
        if (!prefs.configured) return false
        // While you're using LifeHub normally, refresh the saved copy at most every few minutes.
        if (!force && pending().length() == 0 && System.currentTimeMillis() - lastRefresh < 5 * 60_000) return true
        val api = Api(prefs.server, prefs.apiKey, connectTimeout = 6000)
        return try {
            val out = pending()
            if (out.length() > 0) {
                val resp = JSONObject(api.post("/api/v1/sync", JSONObject().put("changes", out).toString()))
                val results = resp.optJSONArray("results") ?: JSONArray()
                val finished = HashSet<String>()
                var error = ""
                for (i in 0 until results.length()) {
                    val r = results.getJSONObject(i)
                    if (r.optString("status") == "error") error = r.optString("message") else finished.add(r.optString("id"))
                }
                synchronized(lock) {
                    // Re-read: you may have added more while this was being sent.
                    val now = pending()
                    val keep = JSONArray()
                    for (i in 0 until now.length()) if (now.getJSONObject(i).optString("id") !in finished) keep.put(now.getJSONObject(i))
                    writeAtomic(outboxFile, keep.toString())
                    resp.optJSONObject("snapshot")?.let { writeAtomic(snapshotFile, it.toString()) }
                }
                lastError = if (error.isEmpty()) "" else "LifeHub couldn't apply a change: $error (it will try again)"
            } else {
                val snap = api.get("/api/v1/offline")
                synchronized(lock) { writeAtomic(snapshotFile, JSONObject(snap).toString()) }
                lastError = ""
            }
            lastRefresh = System.currentTimeMillis()
            online = true
            true
        } catch (e: Api.HttpError) {
            online = e.code != 0
            lastError = e.message ?: "LifeHub answered ${e.code}"
            false
        } catch (e: IOException) {
            online = false
            false
        } catch (e: Exception) {
            online = false
            lastError = e.message ?: ""
            false
        }
    }
}
