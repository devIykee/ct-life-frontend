#!/usr/bin/env python3
"""Mint a LiveKit access token for the LOCAL `livekit-server --dev` only (dev keys devkey/secret).
Spike/testing use: pass it to the app with `adb shell am start ... -e livekitUrl ws://localhost:7880 -e livekitToken <jwt>`.
Usage: python3 client/Tools~/livekit_dev_token.py [identity] [room] [hours]"""
import base64, hashlib, hmac, json, sys, time

def b64(d): return base64.urlsafe_b64encode(d).rstrip(b"=").decode()

identity = sys.argv[1] if len(sys.argv) > 1 else "seeker-spike"
room = sys.argv[2] if len(sys.argv) > 2 else "spike"
hours = float(sys.argv[3]) if len(sys.argv) > 3 else 6
now = int(time.time())
header = {"alg": "HS256", "typ": "JWT"}
claims = {"iss": "devkey", "sub": identity, "name": identity, "nbf": now - 10, "exp": now + int(hours * 3600),
          "video": {"room": room, "roomJoin": True, "canPublish": True, "canSubscribe": True}}
msg = b64(json.dumps(header, separators=(",", ":")).encode()) + "." + b64(json.dumps(claims, separators=(",", ":")).encode())
sig = hmac.new(b"secret", msg.encode(), hashlib.sha256).digest()
print(msg + "." + b64(sig))
