package io.github.shotgn16.lifehub

import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/** Small REST client for LifeHub's /api/v1 (authenticated with this person's API key). */
class Api(private val server: String, private val key: String) {

    class HttpError(val code: Int, message: String) : IOException(message)

    private fun open(path: String, method: String): HttpURLConnection {
        val c = URL(server.trimEnd('/') + path).openConnection() as HttpURLConnection
        c.requestMethod = method
        c.connectTimeout = 15000
        c.readTimeout = 30000
        c.setRequestProperty("X-Api-Key", key)
        c.setRequestProperty("Accept", "application/json")
        return c
    }

    private fun read(c: HttpURLConnection): String {
        val code = c.responseCode
        val stream = if (code in 200..299) c.inputStream else c.errorStream
        val body = stream?.bufferedReader()?.use { it.readText() } ?: ""
        c.disconnect()
        if (code == 401) throw HttpError(code, "The API key wasn't accepted. Copy it again from Settings → Security & API.")
        if (code !in 200..299) throw HttpError(code, "LifeHub answered $code")
        return body
    }

    fun get(path: String): String = read(open(path, "GET"))

    fun post(path: String, json: String): String {
        val c = open(path, "POST")
        c.doOutput = true
        c.setRequestProperty("Content-Type", "application/json")
        c.outputStream.use { it.write(json.toByteArray()) }
        return read(c)
    }

    fun me(): JSONObject = JSONObject(get("/api/v1/me"))
    fun notices(sinceIso: String): JSONArray =
        JSONArray(get("/api/v1/notices" + if (sinceIso.isEmpty()) "" else "?since=" + java.net.URLEncoder.encode(sinceIso, "UTF-8")))
}
