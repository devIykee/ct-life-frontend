# Settings device review — 19 October 2026

> Chronological lane receipt. For the latest installed build, scoped device outcomes and final cleanup, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md). Earlier test counts and pending statements below describe their own checkpoints.

Scope: read-only source/design comparison and review of coordinator-captured Seeker screenshots from `../ct-life-evidence/audit-20261019/`. The coordinator performed the touches; this reviewer did not operate adb or Unity. No UI code or styling changes were made during this review.

## Reviewed outcomes

| Check | Evidence and observed result |
| --- | --- |
| Default Large | `followup-settings-large-top.png` and `followup-settings-size-current.png`: footer explicitly says Large; slider thumb is on the fourth of six ticks. Settings title, profile and text-size row fit. |
| Increase to XXL | Coordinator reports two ordinary large-A taps, Large → Extra large → Largest. `followup-settings-xxl-verified.png` independently shows the Largest footer, thumb at the sixth tick and visibly enlarged text. The footer wraps onto two complete lines. |
| Movement controls at Large/XXL | `followup-settings-large-controls.png` vs `followup-settings-xxl-controls.png`: Analog stick / Arrow buttons remain within their shared segmented control. Help grows from two lines to three, with no clipped words or collision with the Flight controls heading in the shown viewport. |
| Flight controls at Large/XXL | The Simple / Pro labels remain on one line inside the two segments. Simple is visibly selected. The help wraps cleanly at both sizes, retaining its complete Up/Down and view-drag instructions. These captures establish layout, not a Pro-mode interaction test. |
| Recovery row at XXL | `followup-settings-xxl-unstick.png`: Unstick me, its leading icon and the revised “Move back onto safe ground while on foot.” help all fit; surrounding Wallet/Credits rows retain the same grouped-list styling. |
| Recovery aftermath | Coordinator tapped Unstick. `followup-offline-after-unstick.png`, captured about20 seconds later, shows the phone closed and world HUD visible without a persistent veil. The character's shoes/lower legs remain visually obscured by paving. This does not pass avatar grounding; the full-session grounding investigation remains open. The still image does not prove the fade timing or transient toast. |

## Source/design reconciliation

The rows use the existing `DuoSegmented`, `DuoRow`, `Duo.GroupHeader`/`GroupFooter`, icon and grouped-surface classes required by `docs/design/duo-ios-redesign-20261016.md`. The shared size tokens enlarge the shown labels/help; these additions introduce no independent font, radius, spacing or color tokens. Visible labels/help route through `Strings.T`. The recovery overlay uses the existing `--ct-deep` token and clears its input interception on fade-out/cancellation.

`DuoSlider` implements six ordered sizes (XS, S, M, L, XL, XXL). Its large-A button calls `Set(value+1,true)`; Settings persists that through `DuoText.Set` and updates the footer immediately, while PhoneShell applies the matching size class. The rail maps its local X position to the nearest tick on pointer-up; dragging begins only after a14-panel-unit horizontal gesture, allowing vertical scrolling. The coordinator's initially ineffective rail tap, followed by successful increase-button taps, does **not** establish a slider defect. Pointer delivery during that first gesture was not captured.

The earlier file named `followup-settings-xxl-top.png` still showed Large and is not XXL evidence. Use `followup-settings-xxl-verified.png` instead.

Limits: rail drag, persistence across process restart, screen-reader semantics, quantitative contrast, every text size, recovery-busy feedback and Pro-mode touch selection were not established by these screenshots. No additional concrete layout or localization regression was found in the reviewed Large/XXL states.
