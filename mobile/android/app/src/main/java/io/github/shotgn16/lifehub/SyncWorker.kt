package io.github.shotgn16.lifehub

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.usage.UsageEvents
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import org.json.JSONArray
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.concurrent.TimeUnit

/**
 * Every 15 minutes (Android's shortest background interval): shows new LifeHub notifications and,
 * if switched on, sends today's time in the Bible app (measured by Android's Usage access).
 */
class SyncWorker(context: Context, params: WorkerParameters) : Worker(context, params) {

    override fun doWork(): Result {
        val prefs = Prefs(applicationContext)
        if (!prefs.configured) return Result.success()
        val api = Api(prefs.server, prefs.apiKey)
        return try {
            if (prefs.notifications) notices(api, prefs)
            if (prefs.bibleTracking && SetupActivity.usageAccess(applicationContext)) bibleTime(api, prefs)
            Result.success()
        } catch (e: Api.HttpError) {
            Result.success()          // wrong key etc. — nothing to retry
        } catch (e: Exception) {
            Result.retry()            // offline / LifeHub not reachable right now
        }
    }

    private fun notices(api: Api, prefs: Prefs) {
        val first = prefs.lastNotice.isEmpty()
        val list = api.notices(prefs.lastNotice)
        var newest = prefs.lastNotice
        for (i in 0 until list.length()) {
            val n = list.getJSONObject(i)
            val created = n.optString("createdUtc")
            if (created > newest) newest = created
            // On the very first run just remember where we are, instead of replaying old messages.
            if (!first && !n.optBoolean("read")) show(n)
        }
        if (first && newest.isEmpty()) newest = iso(Date())
        prefs.lastNotice = newest
    }

    private fun show(n: JSONObject) {
        val ctx = applicationContext
        if (Build.VERSION.SDK_INT >= 33 &&
            ctx.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return
        val manager = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "LifeHub", NotificationManager.IMPORTANCE_DEFAULT))
        val link = n.optString("link")
        val open = Intent(ctx, MainActivity::class.java)
            .setData(Uri.parse("lifehub://open?path=" + Uri.encode(link)))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        val id = n.optString("id").hashCode()
        val pi = PendingIntent.getActivity(ctx, id, open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val message = n.optString("message")
        val notification = NotificationCompat.Builder(ctx, CHANNEL)
            .setSmallIcon(R.drawable.ic_stat)
            .setColor(0xFF0F766E.toInt())
            .setContentTitle(n.optString("title"))
            .setContentText(message.lineSequence().firstOrNull() ?: "")
            .setStyle(NotificationCompat.BigTextStyle().bigText(message))
            .setContentIntent(pi)
            .setAutoCancel(true)
            .build()
        try { NotificationManagerCompat.from(ctx).notify(id, notification) } catch (_: SecurityException) { }
    }

    /** Rebuilds the last 30 hours of Bible-app sessions from Android's usage events and sends them. */
    private fun bibleTime(api: Api, prefs: Prefs) {
        val usage = applicationContext.getSystemService(Context.USAGE_STATS_SERVICE) as UsageStatsManager
        val end = System.currentTimeMillis()
        val begin = end - TimeUnit.HOURS.toMillis(30)
        val events = usage.queryEvents(begin, end)
        val e = UsageEvents.Event()
        val sessions = mutableListOf<Pair<Long, Long?>>()
        var start: Long? = null
        while (events.hasNextEvent()) {
            events.getNextEvent(e)
            val mine = e.packageName == prefs.bibleApp
            when {
                mine && e.eventType == UsageEvents.Event.ACTIVITY_RESUMED -> if (start == null) start = e.timeStamp
                mine && (e.eventType == UsageEvents.Event.ACTIVITY_PAUSED || e.eventType == UsageEvents.Event.ACTIVITY_STOPPED) ||
                    e.eventType == UsageEvents.Event.SCREEN_NON_INTERACTIVE -> {
                    start?.let { sessions.add(it to e.timeStamp) }
                    start = null
                }
            }
        }
        start?.let { sessions.add(it to null) }       // still reading now

        // Merge quick hops out of the app (answering a message) and drop accidental opens.
        val merged = mutableListOf<Pair<Long, Long?>>()
        for (s in sessions.sortedBy { it.first }) {
            val last = merged.lastOrNull()
            if (last?.second != null && s.first - last.second!! < 120_000) merged[merged.size - 1] = last.first to s.second
            else merged.add(s)
        }
        val kept = merged.filter { it.second == null || it.second!! - it.first >= 45_000 }

        val arr = JSONArray()
        kept.forEach { (s, f) -> arr.put(JSONObject().put("startUtc", iso(Date(s))).apply { if (f != null) put("endUtc", iso(Date(f))) }) }
        val body = JSONObject()
            .put("device", Build.MODEL ?: "Android")
            .put("fromUtc", iso(Date(begin)))
            .put("sessions", arr)
        api.post("/api/v1/bible/sessions", body.toString())
        prefs.lastSync = SimpleDateFormat("EEE HH:mm", Locale.getDefault()).format(Date())
    }

    companion object {
        private const val CHANNEL = "lifehub"
        private const val NAME = "lifehub-sync"

        fun iso(d: Date): String = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US)
            .apply { timeZone = TimeZone.getTimeZone("UTC") }.format(d)

        private val online = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()

        fun schedule(context: Context) {
            WorkManager.getInstance(context).enqueueUniquePeriodicWork(NAME, ExistingPeriodicWorkPolicy.KEEP,
                PeriodicWorkRequestBuilder<SyncWorker>(15, TimeUnit.MINUTES).setConstraints(online).build())
        }

        fun runNow(context: Context) {
            WorkManager.getInstance(context).enqueueUniqueWork("$NAME-now", ExistingWorkPolicy.REPLACE,
                OneTimeWorkRequestBuilder<SyncWorker>().setConstraints(online).build())
        }
    }
}
