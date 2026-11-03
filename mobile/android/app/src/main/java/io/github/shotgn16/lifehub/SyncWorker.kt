package io.github.shotgn16.lifehub

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
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
 * Every 15 minutes (Android's shortest background interval): sends changes made offline, refreshes the
 * offline copy and shows new LifeHub notifications.
 */
class SyncWorker(context: Context, params: WorkerParameters) : Worker(context, params) {

    override fun doWork(): Result {
        val prefs = Prefs(applicationContext)
        if (!prefs.configured) return Result.success()
        val api = Api(prefs.server, prefs.apiKey)
        return try {
            // Send changes made offline and refresh the copy kept on the phone.
            OfflineStore(applicationContext).sync()
            if (prefs.notifications) notices(api, prefs)
            prefs.lastSync = SimpleDateFormat("EEE HH:mm", Locale.getDefault()).format(Date())
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
