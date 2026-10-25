package com.ctlife.location;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.os.Build;
import android.os.Bundle;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import java.util.Locale;

/**
 * One location fix for the opt-in IRL check-in. The game calls request(activity, timeoutMs) and then poll() until it
 * returns "ok,lat,lng,accuracyM,isMock" or "err,reason" (permission | disabled | timeout | unavailable | ...).
 * Nothing is cached: a new request clears the previous answer, and the game drops it after the one POST.
 */
public final class CTLocation {
    private static volatile String result = "";

    private CTLocation() {}

    public static String poll() { return result; }

    public static void clear() { result = ""; }

    public static void request(final Activity activity, final int timeoutMs) {
        result = "";
        activity.runOnUiThread(new Runnable() {
            @Override public void run() { start(activity, timeoutMs); }
        });
    }

    private static void start(final Activity a, final int timeoutMs) {
        try {
            boolean fine = a.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED;
            boolean coarse = a.checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED;
            if (!fine && !coarse) { result = "err,permission"; return; }
            final LocationManager lm = (LocationManager) a.getSystemService(Context.LOCATION_SERVICE);
            if (lm == null) { result = "err,unavailable"; return; }
            String p = null;
            if (Build.VERSION.SDK_INT >= 31 && lm.isProviderEnabled(LocationManager.FUSED_PROVIDER)) p = LocationManager.FUSED_PROVIDER;
            else if (fine && lm.isProviderEnabled(LocationManager.GPS_PROVIDER)) p = LocationManager.GPS_PROVIDER;
            else if (lm.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) p = LocationManager.NETWORK_PROVIDER;
            if (p == null) { result = "err,disabled"; return; }
            final String provider = p;
            final Handler h = new Handler(Looper.getMainLooper());
            if (Build.VERSION.SDK_INT >= 30) {
                final CancellationSignal cs = new CancellationSignal();
                lm.getCurrentLocation(provider, cs, a.getMainExecutor(), new java.util.function.Consumer<Location>() {
                    @Override public void accept(Location l) { if (result.isEmpty()) deliver(l); }
                });
                h.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (!result.isEmpty()) return;
                        cs.cancel();
                        fallback(lm, provider);
                    }
                }, timeoutMs);
            } else {
                final LocationListener listener = new LocationListener() {
                    @Override public void onLocationChanged(Location l) { if (result.isEmpty()) deliver(l); }
                    @Override public void onStatusChanged(String s, int i, Bundle b) {}
                    @Override public void onProviderEnabled(String s) {}
                    @Override public void onProviderDisabled(String s) {}
                };
                lm.requestSingleUpdate(provider, listener, Looper.getMainLooper());
                h.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (!result.isEmpty()) return;
                        lm.removeUpdates(listener);
                        fallback(lm, provider);
                    }
                }, timeoutMs);
            }
        } catch (SecurityException e) {
            result = "err,permission";
        } catch (Exception e) {
            result = "err," + e.getClass().getSimpleName();
        }
    }

    /** No fresh fix in time: a recent last-known position (at most 2 minutes old) is still a real reading. */
    private static void fallback(LocationManager lm, String provider) {
        try {
            Location last = lm.getLastKnownLocation(provider);
            long age = last != null ? (System.currentTimeMillis() - last.getTime()) : Long.MAX_VALUE;
            if (last != null && age < 120000) deliver(last); else result = "err,timeout";
        } catch (SecurityException e) {
            result = "err,permission";
        }
    }

    private static void deliver(Location l) {
        if (l == null) { result = "err,unavailable"; return; }
        boolean mock = Build.VERSION.SDK_INT >= 31 ? l.isMock() : l.isFromMockProvider();
        float acc = l.hasAccuracy() ? l.getAccuracy() : 9999f;
        result = String.format(Locale.ROOT, "ok,%.6f,%.6f,%.1f,%b", l.getLatitude(), l.getLongitude(), acc, mock);
    }
}
