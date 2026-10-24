package com.ctlife.push;

import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

/** FCM callbacks. In the background FCM shows the notification itself; a tap opens the launcher activity with the data extras. */
public final class CTPushService extends FirebaseMessagingService {
    @Override
    public void onMessageReceived(RemoteMessage message) {
        CTPush.onForeground(message);
    }

    @Override
    public void onNewToken(String token) {
        CTPush.onToken(token);
    }
}
