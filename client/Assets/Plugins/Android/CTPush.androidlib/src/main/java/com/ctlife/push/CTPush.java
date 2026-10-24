package com.ctlife.push;

import android.app.Activity;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.util.Log;

import com.google.firebase.FirebaseApp;
import com.google.firebase.messaging.FirebaseMessaging;
import com.google.firebase.messaging.RemoteMessage;

import org.json.JSONObject;

import java.util.Map;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * CT Life push, called from Unity (PushBridge.cs). Every entry point is safe without Firebase config: init() answers false
 * and nothing else touches Firebase.
 */
public final class CTPush {
    private static final String TAG = "CTPush";
    public static final String CHANNEL = "ctlife_default";
    private static volatile boolean ready;
    private static volatile String token, tokenError;
    private static final ConcurrentLinkedQueue<String> inbox = new ConcurrentLinkedQueue<>();

    private CTPush() { }

    /** True when FirebaseApp is (or can be) initialised from the bundled resource values. */
    public static boolean init(Activity activity) {
        if (ready) return true;
        try {
            Context ctx = activity.getApplicationContext();
            FirebaseApp app = FirebaseApp.getApps(ctx).isEmpty() ? FirebaseApp.initializeApp(ctx) : FirebaseApp.getInstance();
            if (app == null) { Log.i(TAG, "No Firebase config in this build: push disabled"); return false; }
            if (Build.VERSION.SDK_INT >= 26) {
                NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
                if (nm != null && nm.getNotificationChannel(CHANNEL) == null)
                    nm.createNotificationChannel(new NotificationChannel(CHANNEL, "CT Life", NotificationManager.IMPORTANCE_DEFAULT));
            }
            ready = true;
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "Firebase init failed: push disabled", t);
            return false;
        }
    }

    public static void requestToken() {
        if (!ready) return;
        try {
            FirebaseMessaging.getInstance().getToken().addOnCompleteListener(task -> {
                if (task.isSuccessful()) { token = task.getResult(); tokenError = null; }
                else tokenError = task.getException() != null ? task.getException().getClass().getSimpleName() : "unknown";
            });
        } catch (Throwable t) { tokenError = t.getClass().getSimpleName(); }
    }

    /** Sign-out: drop this install's token at FCM so the next account gets a fresh one. No-op without Firebase. */
    public static void deleteToken() {
        token = null; tokenError = null;
        if (!ready) return;
        try {
            FirebaseMessaging.getInstance().deleteToken().addOnCompleteListener(task -> {
                if (!task.isSuccessful()) Log.w(TAG, "deleteToken failed", task.getException());
                else Log.i(TAG, "FCM token deleted");
            });
        } catch (Throwable t) { Log.w(TAG, "deleteToken failed", t); }
    }

    public static String token() { return token; }
    public static String tokenError() { return tokenError; }

    static void onToken(String t) { token = t; }

    static void onForeground(RemoteMessage m) {
        try {
            JSONObject o = new JSONObject();
            RemoteMessage.Notification n = m.getNotification();
            if (n != null) { o.put("title", n.getTitle()); o.put("body", n.getBody()); }
            for (Map.Entry<String, String> e : m.getData().entrySet()) o.put(e.getKey(), e.getValue());
            inbox.add(o.toString());
        } catch (Throwable t) { Log.w(TAG, "message dropped", t); }
    }

    /** The next foreground message as JSON, or null. */
    public static String poll() { return inbox.poll(); }

    /**
     * The notification tap's deep_link (FCM puts the data map in the launcher intent's extras) or a ctlife:// data URI,
     * consumed once so a later focus does not reopen it.
     */
    public static String takeLaunchLink(Activity activity) {
        Intent i = activity.getIntent();
        if (i == null) return null;
        String link = i.getStringExtra("deep_link");
        if (link != null) { i.removeExtra("deep_link"); return link; }
        Uri data = i.getData();
        if (data != null && "ctlife".equalsIgnoreCase(data.getScheme())) { i.setData(null); return data.toString(); }
        return null;
    }
}
