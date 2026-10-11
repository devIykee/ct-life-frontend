using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.Rest.Model;
using UnityEngine;
using UnityEngine.UIElements;
using Space = CTLife.Rest.Model.Space;

namespace CTLife.UI
{
    /// <summary>Phase-three room UI. Normal flow reads server state. Guarded visual review never creates transport or capture.</summary>
    public sealed class SpacesApp : PhoneApp
    {
        public override string Id => "spaces";
        public override string Title => SpacesStrings.T("title");
        public override string Color => "#a55eea"; // Existing Spaces catalog token.
        public override int Phase => 3;
        public override string Feature => Features.Voice;
        private View _view;
        // Retain an ambiguous create across closing/reopening the phone in this process.
        private string _createKey, _createTitle, _createUser;
        public override VisualElement Open()
        {
            Closed();
            _view = new View(this, Phone.Close);
            return _view.Root;
        }
        public override void Closed() { _view?.Dispose(); _view = null; }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public static VisualElement BuildReview(string scenario, Action close = null) => new View(new SpacesApp(), close ?? (() => { }), scenario).Root;
        public static bool IsIsolatedReview(VisualElement root) => root?.userData is View view && view.ReviewIsolated;
#endif
        private sealed class View : IDisposable
        {
            public readonly ScrollView Root;
            private readonly SpacesApp _owner;
            private readonly Action _close;
            private readonly Api _api;
            private readonly SpacesApi _spaces;
            private readonly Guid _me;
            private readonly Session _session;
            private readonly string _sessionUserId;
            private readonly CancellationTokenSource _life = new CancellationTokenSource();
            private readonly Subscriptions _subs = new Subscriptions();
            private readonly Dictionary<Guid, string> _names = new Dictionary<Guid, string>();
            private readonly List<Button> _actions = new List<Button>();
            private readonly Dictionary<Guid, Label> _activity = new Dictionary<Guid, Label>();
            private readonly VisualElement _content;
            private readonly Label _message, _busyLabel;
            private TextField _titleInput;
            private List<Space> _rooms;
            private Space _room;
            private SpacesAudio _audio;
            private string _draft = "";
            private bool _busy, _disposed, _available, _fresh, _confirmEnd, _report, _editing, _staleHandled;
            private double _checkedAt, _nextPoll;
            private int _revision, _visibleHands = 12, _visibleRooms = 25;
            private Label _audioStatus, _micStatus;
            private Button _mic, _mute;
            private bool _showCompose;
            private readonly bool _reviewOnly;
            private bool _reviewConnected, _reviewMuted;
            private bool AudioConnected => _reviewOnly ? _reviewConnected : _audio?.Connected == true;
            private bool AudioMuted => _reviewOnly ? _reviewMuted : _audio?.Muted == true;

            public View(SpacesApp owner, Action close, string reviewScenario = null)
            {
                _owner = owner; _close = close; _reviewOnly = reviewScenario != null;
                _message = Copy("loading", "ct-space-message");
                _busyLabel = Copy("busy", "ct-small ct-muted");
                _content = El.Div("ct-space-content");
                Root = AppUi.Page(_message, _busyLabel, _content);
                Root.AddToClassList("ct-spaces");
                Root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
                var sheet = Resources.Load<StyleSheet>("UI/spaces");
                if (sheet != null) Root.styleSheets.Add(sheet);
                if (_reviewOnly)
                {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                    Root.userData = this;
                    _me = Guid.Parse("a1100000-0000-4000-8000-000000000001");
                    SeedReview(reviewScenario);
#endif
                    return;
                }
                _api = Services.Get<Api>(); _spaces = _api.Spaces;
                _session = Services.Get<Session>(); _sessionUserId = _session.UserId;
                Guid.TryParse(_sessionUserId, out _me);
                if (_owner._createUser != _sessionUserId) { _owner._createKey = null; _owner._createTitle = null; }
                _owner._createUser = _sessionUserId;
                _subs.On<SpaceUpdateMessage>(OnInvalidated);
                _subs.On<DegradedChangedSignal>(_ => { if (!Features.IsAvailable(Features.Voice)) { DropAudio(); _fresh = false; Render(); } });
                _ = Runner.Instance;
                Runner.Ticked += Tick;
                _session.Changed += SessionChanged;
                if (_session.IsSignedIn && _me != Guid.Empty) Run(LoadList);
                else { SetMessage("sessionEnded"); Dispose(); }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _life.Cancel();
                Root.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
                _subs.Dispose();
                Runner.Ticked -= Tick;
                if (_session != null) _session.Changed -= SessionChanged;
                DropAudio();
            }

            private void OnDetached(DetachFromPanelEvent evt) { if (ReferenceEquals(evt.target, Root)) Dispose(); }

            private void SessionChanged()
            {
                if (_disposed || (_session.IsSignedIn && _session.UserId == _sessionUserId)) return;
                _owner._createKey = null; _owner._createTitle = null; _owner._createUser = null;
                Dispose();
                _close();
            }

            private async void Run(Func<CancellationToken, Task> work)
            {
                if (_disposed || _busy || _reviewOnly) return;
                _busy = true; SetBusy();
                try { await work(_life.Token); }
                catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (_disposed) return;
                    _fresh = false;
                    _audio?.MuteMicrophone();
                    if (SpacesPolicy.RouteUnavailable(error))
                    {
                        if (_room == null) _available = false;
                        SetMessage(_room == null ? "unavailable" : "unavailableRoom");
                    }
                    else SetMessage(error is SpacesAudioException voice ? voice.Key
                        : error is TimeoutException ? "timeout" : error is ApiException api && api.IsNetwork ? "network" : "failed");
                    Render();
                    // Do not log token-bearing SDK exceptions or server response bodies.
                    Log.Warn("spaces", error is ApiException a ? $"Request failed: {a.Code} / {a.Status}" : error.GetType().Name);
                }
                finally { _busy = false; if (!_disposed) SetBusy(); }
            }

            private void Tick()
            {
                if (_disposed || _reviewOnly) return;
                var now = Time.realtimeSinceStartupAsDouble;
                if (_room != null && now - _checkedAt > 8 && !_staleHandled) { Invalidate(); }
                if (_busy || (_room == null && _editing) || now < _nextPoll || !Features.IsAvailable(Features.Voice)) return;
                _nextPoll = now + (_room == null ? 15 : 5);
                Run(_room == null ? LoadList : RefreshRoom);
            }
            private void OnInvalidated(SpaceUpdateMessage update)
            {
                if (_room != null && !string.Equals(update.spaceId, _room.SpaceId.ToString(), StringComparison.OrdinalIgnoreCase)) return;
                _revision++;
                if (_room != null) { _fresh = false; _audio?.InvalidateRoster(); SetBusy(); } // Backend revocation unpublishes tracks; refresh without cutting every speaker on listener-count updates.
                _nextPoll = Time.realtimeSinceStartupAsDouble;
            }
            private void Invalidate()
            {
                _staleHandled = true;
                _fresh = false;
                _audio?.InvalidateRoster();
                _audio?.MuteMicrophone();
                SetMessage("stale");
                SetBusy();
            }

            private async Task LoadList(CancellationToken ct)
            {
                _nextPoll = Time.realtimeSinceStartupAsDouble + 15;
                var list = await _spaces.Live(ct);
                ct.ThrowIfCancellationRequested();
                if (list == null || list.Any(x => !SpacesPolicy.Valid(x))) throw new SpacesAudioException("invalid");
                _rooms = list.GroupBy(x => x.SpaceId).Select(x => x.First()).ToList();
                _available = true;
                SetMessage(_owner._createKey != null ? "createUncertain" : null);
                Render();
            }
            private async Task RefreshRoom(CancellationToken ct)
            {
                if (_room == null) return;
                var revision = _revision;
                var room = await _spaces.Get(_room.SpaceId, ct);
                ct.ThrowIfCancellationRequested();
                if (!SpacesPolicy.Valid(room) || room.SpaceId != _room.SpaceId) throw new SpacesAudioException("invalid");
                _room = room;
                _fresh = revision == _revision;
                _staleHandled = false;
                _checkedAt = Time.realtimeSinceStartupAsDouble;
                _nextPoll = _checkedAt + (_fresh ? 5 : 0);
                _audio?.SetRoster(_fresh ? room : null, _me);
                SetMessage(_fresh ? null : "stale");
                Render();
                await LoadNames(ct);
            }
            private async Task LoadNames(CancellationToken ct)
            {
                // Bounded batch; fallback identifies real IDs instead of inventing people.
                var ids = new[] { _room.HostId }.Concat(SpacesPolicy.Speakers(_room)).Concat(SpacesPolicy.Hands(_room).Take(_visibleHands))
                    .Distinct().Where(id => !_names.ContainsKey(id)).Take(24).ToArray();
                await Task.WhenAll(ids.Take(12).Select(async id =>
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var profile = await SpacesRequest.Run(c => _api.GetUser(id.ToString(), c), ct, 5000);
                        if (profile != null && profile.Id == id && !string.IsNullOrWhiteSpace(profile.Alias)) _names[id] = profile.Alias;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { _names[id] = SpacesStrings.F("nameFallback", id.ToString().Substring(0, 6)); }
                }));
                ct.ThrowIfCancellationRequested();
                Render();
            }

            private async Task OpenRoom(Guid id, CancellationToken ct)
            {
                var room = await _spaces.Get(id, ct);
                if (!SpacesPolicy.Valid(room) || room.SpaceId != id) throw new SpacesAudioException("invalid");
                _room = room; _report = false; _confirmEnd = false;
                await RefreshRoom(ct);
                await JoinAudio(ct);
            }
            private async Task Create(CancellationToken ct)
            {
                if (_owner._createKey == null)
                {
                    if (string.IsNullOrWhiteSpace(_draft)) { SetMessage("topicRequired"); return; }
                    _owner._createTitle = _draft.Trim();
                    _owner._createKey = Ids.NewIdempotencyKey();
                }
                Space created;
                try { created = await _spaces.Create(_owner._createTitle, _owner._createKey, ct); }
                catch (ApiException error) when (error.Status == 400 || error.Status == 401 || error.Status == 403 || error.Status == 404 || error.Status == 422 || error.Status == 501)
                {
                    _draft = _owner._createTitle;
                    _owner._createKey = null; _owner._createTitle = null;
                    throw;
                }
                if (!SpacesPolicy.Valid(created)) throw new SpacesAudioException("invalid");
                _owner._createKey = null; _owner._createTitle = null; _draft = "";
                await OpenRoom(created.SpaceId, ct);
            }
            private async Task JoinAudio(CancellationToken ct)
            {
                await RefreshRoom(ct);
                if (!_fresh || !Features.IsAvailable(Features.Voice)) throw new SpacesAudioException("roleChanged");
                var token = await SpacesRequest.Run(c => _api.RtcToken("space", _room.SpaceId.ToString(), c), ct);
                if (Services.TryGet<VoiceService>(out var nearby))
                    await SpacesRequest.Run(async _ => { await nearby.Disconnect(); return true; }, ct, 20000);
                ct.ThrowIfCancellationRequested();
                DropAudio();
                _audio = new SpacesAudio();
                _audio.Changed += UpdateAudio;
                _audio.Failed += SetMessage;
                SetMessage("connecting");
                await _audio.Connect(token, _fresh ? _room : null, _me, ct);
                SetMessage(null);
                Render();
            }
            private void DropAudio()
            {
                var audio = _audio; _audio = null;
                if (audio == null) return;
                audio.Changed -= UpdateAudio; audio.Failed -= SetMessage; audio.Dispose();
            }
            private async Task ToggleMic(CancellationToken ct)
            {
                if (_audio == null) return;
                if (_audio.MicOn) { _audio.MuteMicrophone(); return; }
                await RefreshRoom(ct);
                if (!_fresh || !SpacesPolicy.CanSpeak(_room, _me)) throw new SpacesAudioException("roleChanged");
                var token = await SpacesRequest.Run(c => _api.RtcToken("space", _room.SpaceId.ToString(), c), ct);
                if (!_fresh || token == null || !token.CanPublish) throw new SpacesAudioException("roleChanged");
                await _audio.EnableMicrophone(token.CanPublish, ct);
                UpdateAudio();
            }
            private async Task ChangeRole(Guid target, bool promote, CancellationToken ct)
            {
                await RefreshRoom(ct);
                if (!_fresh || !SpacesPolicy.IsHost(_room, _me) || (promote && !SpacesPolicy.CanPromote(_room, _me, target))
                    || (!promote && !SpacesPolicy.Speakers(_room).Contains(target))) throw new SpacesAudioException("changed");
                if (promote) await _spaces.Promote(_room.SpaceId, target, ct);
                else await _spaces.Demote(_room.SpaceId, target, ct);
                await RefreshRoom(ct);
            }
            private async Task Hand(CancellationToken ct)
            {
                await RefreshRoom(ct);
                if (!_fresh || SpacesPolicy.CanSpeak(_room, _me)) throw new SpacesAudioException("changed");
                await _spaces.Hand(_room.SpaceId, !SpacesPolicy.Hands(_room).Contains(_me), ct);
                await RefreshRoom(ct);
            }
            private async Task End(CancellationToken ct)
            {
                await RefreshRoom(ct);
                if (!_fresh || !SpacesPolicy.IsHost(_room, _me)) throw new SpacesAudioException("changed");
                await _spaces.End(_room.SpaceId, ct);
                DropAudio(); _room = null; _confirmEnd = false;
                await LoadList(ct);
                SetMessage("ended");
            }

            private void Render()
            {
                if (_disposed) return;
                var scroll = Root.scrollOffset;
                _actions.Clear(); _activity.Clear(); _audioStatus = null; _micStatus = null; _mic = null; _mute = null;
                _content.Clear(); _titleInput = null;
                if (_reviewOnly) _content.Add(Copy("review", "ct-space-review"));
                if (_room == null) RenderList(); else RenderRoom();
                SetBusy();
                Root.schedule.Execute(() => { if (!_disposed) Root.scrollOffset = scroll; });
            }
            private void RenderList()
            {
                _content.Add(El.Div("ct-space-list-heading", Copy("heading", "ct-h1"), Action("refresh", LoadList)));
                _content.Add(Copy("intro", "ct-small ct-muted"));
                if (!_available)
                {
                    _content.Add(El.Div("ct-card", Copy("unavailable", "ct-h2"), Copy("unavailableDetail", "ct-body")));
                    _content.Add(LocalButton("city", _close)); return;
                }
                _content.Add(LocalButton(_showCompose ? "closeCompose" : "hostRoom", () => { _showCompose = !_showCompose; Render(); }));
                if (_showCompose || _owner._createKey != null)
                {
                    var input = new TextField(SpacesStrings.T("topic")) { value = _owner._createTitle ?? _draft, maxLength = 120 };
                    _titleInput = input; input.name = "spaces-topic";
                    input.AddToClassList("ct-space-input"); input.tooltip = SpacesStrings.T("topicHint");
                    input.SetEnabled(_owner._createKey == null);
                    input.RegisterValueChangedCallback(evt => _draft = evt.newValue);
                    input.RegisterCallback<FocusInEvent>(_ => _editing = true);
                    input.RegisterCallback<FocusOutEvent>(_ => _editing = false);
                    _content.Add(El.Div("ct-card ct-space-compose", input, Copy("createHint", "ct-small ct-muted"),
                        Action(_owner._createKey == null ? "create" : "createRetry", Create, true)));
                    if (_owner._createKey != null) _content.Add(Copy("createUncertain", "ct-small"));
                }
                _content.Add(Copy("live", "ct-eyebrow"));
                if (_rooms == null || _rooms.Count == 0)
                    _content.Add(El.Div("ct-card", Copy("empty", "ct-h2"), Copy("emptyDetail", "ct-body ct-muted")));
                foreach (var room in (_rooms ?? new List<Space>()).Take(_visibleRooms))
                    _content.Add(El.Div("ct-card ct-space-room", Raw(room.Title, "ct-h2"),
                        Raw(SpacesStrings.F("listeners", room.ListenerCount), "ct-small ct-muted"), Action("listen", ct => OpenRoom(room.SpaceId, ct))));
                if (_rooms?.Count > _visibleRooms) _content.Add(LocalButton("moreRooms", () => { _visibleRooms += 25; Render(); }));
            }
            private void RenderRoom()
            {
                _content.Add(Raw(_room.Title, "ct-h1"));
                _content.Add(Raw(SpacesStrings.F("listeners", _room.ListenerCount), "ct-small ct-muted"));
                _audioStatus = Copy("disconnected", "ct-space-audio"); _micStatus = Copy("micSilent", "ct-eyebrow");
                _content.Add(El.Div("ct-space-status", _audioStatus, _micStatus));
                _content.Add(Copy("audioHint", "ct-small ct-muted"));
                var controls = El.Div("ct-space-controls");
                if (AudioConnected)
                {
                    _mute = LocalButton(AudioMuted ? "unmute" : "mute", () => { if (_reviewOnly) { _reviewMuted = !_reviewMuted; UpdateAudio(); } else _audio?.SetMuted(!_audio.Muted); });
                    _mute.name = "spaces-mute";
                    controls.Add(_mute);
                    if (SpacesPolicy.CanSpeak(_room, _me))
                    {
                        _mic = El.Btn(SpacesStrings.T("micOn"), () =>
                        {
                            if (_audio?.MicOn == true) _audio.MuteMicrophone();
                            else Run(ToggleMic);
                        }, "ct-btn ct-btn-primary");
                        _mic.name = "spaces-mic"; controls.Add(_mic);
                    }
                    else controls.Add(Action(SpacesPolicy.Hands(_room).Contains(_me) ? "lower" : "raise", Hand, true));
                }
                else controls.Add(Action("reconnect", JoinAudio, true));
                controls.Add(Action("refresh", RefreshRoom));
                _content.Add(controls);
                _content.Add(Copy(SpacesPolicy.CanSpeak(_room, _me) ? "speakingHint" : "handHint", "ct-small ct-muted"));
                _content.Add(Copy("stage", "ct-eyebrow"));
                AddPerson(_room.HostId, "host", null);
                bool host = SpacesPolicy.IsHost(_room, _me);
                foreach (var user in SpacesPolicy.Speakers(_room)) AddPerson(user, "speaker", host ? Action("audience", ct => ChangeRole(user, false, ct), role: true) : null);
                if (host)
                {
                    var hands = SpacesPolicy.Hands(_room);
                    _content.Add(Raw(SpacesStrings.F("queue", hands.Count), "ct-eyebrow"));
                    if (hands.Count == 0) _content.Add(Copy("queueEmpty", "ct-small ct-muted"));
                    bool full = SpacesPolicy.Speakers(_room).Count >= SpacesPolicy.SpeakerLimit;
                    if (full) _content.Add(Copy("full", "ct-small ct-muted"));
                    foreach (var user in hands.Take(_visibleHands)) AddPerson(user, "listener", full ? null : Action("invite", ct => ChangeRole(user, true, ct), role: true));
                    if (hands.Count > _visibleHands) _content.Add(El.Btn(SpacesStrings.T("moreHands"), () => { _visibleHands += 12; Run(RefreshRoom); }));
                }
                if (_reviewOnly) _content.Add(LocalButton("leave", () => { _reviewConnected = false; _room = null; _report = false; _confirmEnd = false; SetMessage(null); Render(); }));
                else _content.Add(Action("leave", async ct => { DropAudio(); _room = null; _report = false; _confirmEnd = false; await LoadList(ct); }));
                _content.Add(LocalButton("report", () => { _report = !_report; Render(); }));
                if (_report) RenderReport();
                if (host)
                {
                    _content.Add(LocalButton("end", () => { _confirmEnd = !_confirmEnd; Render(); }));
                    if (_confirmEnd) _content.Add(El.Div("ct-card", Copy("endAsk", "ct-h2"), Copy("endDetail", "ct-body"),
                        Action("confirmEnd", End, role: true), LocalButton("cancel", () => { _confirmEnd = false; Render(); })));
                }
                UpdateAudio();
            }
            private void RenderReport()
            {
                if (_reviewOnly) { var review = El.Div("ct-card", Copy("reportTitle", "ct-h2"), Copy("reviewReport", "ct-body")); review.name = "spaces-report-panel"; _content.Add(review); return; }
                var reasons = Services.Get<ConfigStore>().Registries.ReportReasons;
                var section = El.Div("ct-card", Copy("reportTitle", "ct-h2"));
                if (reasons == null || reasons.Count == 0) section.Add(Copy("reportUnavailable", "ct-body"));
                else foreach (var reason in reasons)
                {
                    var captured = reason;
                    var button = El.Btn(Strings.ReportReason(reason.id, reason.name), () => Run(async ct =>
                    {
                        await SpacesRequest.Run(async c => { await _api.Report("space", _room.SpaceId.ToString(), captured.id, ct: c); return true; }, ct);
                        _report = false; Render(); SetMessage("reportSent");
                    }));
                    _actions.Add(button); section.Add(button);
                }
                _content.Add(section);
            }
            private void AddPerson(Guid id, string role, Button action)
            {
                var name = _names.TryGetValue(id, out var alias) ? alias : SpacesStrings.F("nameFallback", id.ToString().Substring(0, 6));
                if (id == _me) name = SpacesStrings.F("you", name);
                var activity = Raw("", "ct-space-speaking"); _activity[id] = activity;
                var row = El.Div("ct-space-person", El.Div("ct-grow", Raw(name, "ct-h2"), Copy(role, "ct-small ct-muted"), activity));
                if (action != null) row.Add(action);
                _content.Add(row);
            }
            private Button Action(string key, Func<CancellationToken, Task> action, bool primary = false, bool role = false)
            {
                var button = El.Btn(SpacesStrings.T(key), () => Run(action), primary ? "ct-btn ct-btn-primary" : "ct-btn");
                if (role) button.AddToClassList("ct-space-role-action");
                button.name = "spaces-" + key;
                _actions.Add(button);
                return button;
            }
            private void SetBusy()
            {
                _busyLabel.Show(_busy);
                _titleInput?.SetEnabled(!_busy && _owner._createKey == null);
                bool available = !_reviewOnly && Features.IsAvailable(Features.Voice);
                foreach (var action in _actions) action.SetEnabled(!_busy && available && (!action.ClassListContains("ct-space-role-action") || _fresh));
                UpdateAudio();
            }
            private void UpdateAudio()
            {
                if (_disposed) return;
                if (_audioStatus != null) _audioStatus.text = SpacesStrings.T(AudioConnected ? (AudioMuted ? "muted" : "connected") : "disconnected");
                if (_micStatus != null) _micStatus.text = SpacesStrings.T(_audio?.MicOn == true ? "micLive" : "micSilent");
                if (_mic != null)
                {
                    _mic.text = SpacesStrings.T(_audio?.MicOn == true ? "micOff" : "micOn");
                    _mic.SetEnabled(!_reviewOnly && (_audio?.MicOn == true || (!_busy && _fresh && _audio?.Connected == true && !_audio.MicBusy)));
                    // Turning off stays usable during a background refresh.
                }
                if (_mute != null) _mute.text = SpacesStrings.T(AudioMuted ? "unmute" : "mute");
                foreach (var entry in _activity) entry.Value.text = _audio?.IsSpeaking(entry.Key) == true ? SpacesStrings.T("speaking") : "";
            }
            private void SetMessage(string key) { if (!_disposed) { _message.text = key == null ? "" : SpacesStrings.T(key); _message.Show(key != null); } }
            private static Button LocalButton(string key, Action action)
            {
                var button = El.Btn(SpacesStrings.T(key), action); button.name = "spaces-" + key; return button;
            }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            public bool ReviewIsolated => _reviewOnly && !_disposed && _api == null && _spaces == null && _session == null && _audio == null;
            private void SeedReview(string scenario)
            {
                var host = Guid.Parse("a1100000-0000-4000-8000-000000000002");
                var speaker = Guid.Parse("a1100000-0000-4000-8000-000000000003");
                _names[_me] = SpacesStrings.T("reviewMe"); _names[host] = SpacesStrings.T("reviewHost"); _names[speaker] = SpacesStrings.T("reviewSpeaker");
                var room = new Space(Guid.Parse("a2200000-0000-4000-8000-000000000001"), SpacesStrings.T("reviewTitle"), host, new List<Guid>{speaker}, new List<Guid>(), 7);
                _rooms = new List<Space> { room, new Space(Guid.Parse("a2200000-0000-4000-8000-000000000002"), SpacesStrings.T("reviewOther"), speaker, new List<Guid>(), new List<Guid>(), 3) };
                _available = scenario != "unavailable"; _fresh = true; _checkedAt = Time.realtimeSinceStartupAsDouble;
                SetMessage(null);
                switch (scenario)
                {
                    case "list": case "unavailable": break;
                    case "listening": _room = room; _reviewConnected = true; break;
                    case "host": _room = new Space(room.SpaceId, room.Title, _me, new List<Guid>{speaker}, new List<Guid>{host}, 7); _reviewConnected = true; break;
                    case "role-change": _room = room; _reviewConnected = true; SetMessage("roleChanged"); break;
                    case "pending": _owner._createKey = "visual-review-only"; _owner._createTitle = SpacesStrings.T("reviewOther"); _showCompose = true; SetMessage("createUncertain"); break;
                    case "error": _room = room; _fresh = false; SetMessage("network"); break;
                    case "ended": _rooms.Clear(); SetMessage("ended"); break;
                    default: throw new ArgumentOutOfRangeException(nameof(scenario));
                }
                Render();
            }
#endif
            private static Label Copy(string key, string cls) => Raw(SpacesStrings.T(key), cls);
            private static Label Raw(string copy, string cls) { var label = El.Text(copy, cls); label.enableRichText = false; return label; }
        }
    }
}
