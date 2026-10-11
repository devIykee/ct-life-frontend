#!/usr/bin/env bash
# Launch CT Life on the Seeker with development launch extras (dev builds only).
# Writes a fresh automation key into the app's private files (adb run-as) and passes it, as LaunchArgs requires.
#   tools/integration/seeker_launch.sh [extras...]      e.g. -e autoenter 1 -e venue perp_pit
ADB="${ADB:-$HOME/Library/Android/sdk/platform-tools/adb}"; S="${SEEKER:-SM02G40619141343}"
PKG=com.ctlife.app; ACT=com.unity3d.player.UnityPlayerGameActivity
KEY="$(openssl rand -hex 24)"
"$ADB" -s "$S" shell am force-stop $PKG
"$ADB" -s "$S" shell "run-as $PKG sh -c 'mkdir -p files && echo $KEY > files/automation.key'" || echo "!! run-as failed: extras will be ignored"
for p in 4010 2567 7880 7881; do "$ADB" -s "$S" reverse tcp:$p tcp:$p >/dev/null; done
"$ADB" -s "$S" shell am start -n $PKG/$ACT -e automationKey "$KEY" "$@" >/dev/null
