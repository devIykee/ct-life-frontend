using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.World.Likeness;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Private photo → exact-reference approval → actual rigged-model review → server acceptance.</summary>
    public sealed class CharacterLikenessPanel
    {
        private readonly string _modelId, _name, _species;
        private VisualElement _root, _picture, _form, _actions;
        private Label _title, _body, _status, _price;
        private Button _primary, _choose, _retry, _cancel, _deletePhoto, _copyReference;
        private Toggle _own, _provider;
        private Texture2D _photo;
        private byte[] _photoBytes, _modelBytes;
        private CancellationTokenSource _lifetime;
        private LikenessClient _api;
        private Session _session;
        private string _owner, _jobId, _uploadId, _entitlementId, _referenceHash, _previewJob, _operationKey;
        private bool _busy, _enabled, _closed, _watching, _checkoutAvailable, _signAttempted;
        private string _checkoutId, _signature;
        private LikenessCheckout _checkout;
        private LikenessLoadedAvatar _asset;
        private LikenessModelPreview _modelPreview;
        private LikenessAssetManifest _manifest;
        private Action _primaryAction;
        public event Action Closed;
        public event Action<LikenessAssetManifest> Accepted;
        public CharacterLikenessPanel(string modelId, string name, string species) { _modelId = modelId; _name = name; _species = species; }
        private string StoreKey => "ctlife." + Env.Current.id + ".likeness." + _owner + "." + _modelId;
        private CancellationToken Token => _lifetime.Token;
        private bool Alive => !_closed && _root != null && _session?.UserId == _owner;

        public void Show(VisualElement parent)
        {
            Close(); _closed = false;
            _lifetime = new CancellationTokenSource();
            _title = Text("likeness.title", "lk-title");
            _body = Text("likeness.intro", "lk-body");
            _status = Text("likeness.checking", "lk-status");
            _price = Text("likeness.noPrice", "lk-price");
            _picture = El.Div("lk-picture");
            _picture.Add(Text("likeness.photoHint", "lk-photo-hint"));
            _choose = Button("likeness.choose", () => Run(PickPhoto), "lk-secondary"); _choose.name = "likeness-choose-photo";
            _own = new Toggle { text = Strings.T("likeness.ownConsent") }; _own.AddToClassList("lk-consent");
            _provider = new Toggle { text = Strings.T("likeness.providerConsent") }; _provider.AddToClassList("lk-consent");
            _own.RegisterValueChangedCallback(_ => RefreshStart()); _provider.RegisterValueChangedCallback(_ => RefreshStart());
            var privacy = Button("likeness.privacy", () => Application.OpenURL("https://www.tripo3d.ai/privacy"), "lk-link");
            _form = El.Div("lk-form", _own, _provider, Text("likeness.retention", "lk-retention"), privacy);
            _primary = Button("likeness.start", () => _primaryAction?.Invoke(), "lk-primary"); _primary.name = "likeness-primary";
            _retry = Button("likeness.retry", () => Run(Resume), "lk-secondary"); _retry.name = "likeness-retry";
            _cancel = Button("likeness.cancel", () => Run(CancelCreation), "lk-link");
            _deletePhoto = Button("likeness.deletePhoto", () => Run(DeletePhoto), "lk-link");
            _copyReference = Button("likeness.copyReference", () => {
                if (string.IsNullOrEmpty(_jobId)) return;
                GUIUtility.systemCopyBuffer = _jobId;
                _status.text = Strings.F("likeness.referenceCopied", _jobId);
            }, "lk-link");
            _actions = El.Div("lk-actions", _primary, _retry, _cancel, _deletePhoto, _copyReference);
            var close = Button("likeness.close", Close, "lk-close"); close.name = "likeness-close";
            var copy = El.VerticalScroll("lk-copy-scroll");
            copy.Add(_body); copy.Add(_price); copy.Add(_form); copy.Add(_status);
            var right = El.Div("lk-details", copy, _actions);
            var sheet = El.Div("lk-sheet", El.Div("lk-header", _title, close), El.Div("lk-columns", El.Div("lk-media", _picture, _choose), right));
            _root = El.Div("lk-overlay", sheet); _root.name = "ob-likeness-panel";
            OnboardingFrame.AddSheet(_root, "UI/likeness");
            _root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); } });
            int pointer = -1;
            _picture.RegisterCallback<PointerDownEvent>(e => { if (_asset == null) return; pointer = e.pointerId; _picture.CapturePointer(pointer); });
            _picture.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == pointer) _modelPreview?.Spin(e.deltaPosition.x); });
            _picture.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == pointer) { _picture.ReleasePointer(pointer); pointer = -1; } });
            parent.Add(_root);
            _primaryAction = () => Run(StartCreation);
            _primary.SetEnabled(false); Display(_retry, false); Display(_cancel, false); Display(_deletePhoto, false); Display(_copyReference, false);
            if (!Services.TryGet<Session>(out _session) || !Services.TryGet<Api>(out var api) || !_session.IsSignedIn)
            { _status.text = Strings.T("likeness.sessionChanged"); return; }
            _owner = _session.UserId; _api = new LikenessClient(api.Rest, _session);
            _session.Changed += SessionChanged;
            RestoreIntent();
            Run(Resume);
        }

        private async void Run(Func<Task> work)
        {
            if (_busy || !Alive) return;
            _busy = true; _primary.SetEnabled(false); _retry.SetEnabled(false); _choose.SetEnabled(false);
            try { await work(); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (Alive)
                {
                    _status.text = Strings.T(e is InvalidDataException ? "likeness.badModel" : "likeness.offline");
                    Display(_retry, true);
                }
            }
            finally
            {
                _busy = false;
                if (Alive) { _retry.SetEnabled(true); _choose.SetEnabled(string.IsNullOrEmpty(_jobId)); RefreshStart(); }
            }
        }

        private async Task Resume()
        {
            Display(_retry, false);
            _status.text = Strings.T("likeness.checking");
            var availability = await _api.Availability(Token);
            if (!Alive) return;
            var model = (availability["supportedModels"] as JArray)?.OfType<JObject>().FirstOrDefault(x => x.Value<string>("modelId") == _modelId);
            _enabled = _species == "human" && availability.Value<bool?>("enabled") == true && availability.Value<bool?>("photoUploadEnabled") == true && model?.Value<bool?>("enabled") == true;
            _entitlementId = (availability["entitlement"] as JObject)?.Value<string>("id");
            _checkoutAvailable = availability.Value<bool?>("checkoutAvailable") == true;
            string price = (availability["price"] as JObject)?.Value<string>("display");
            _price.text = !string.IsNullOrEmpty(_entitlementId) ? Strings.T("likeness.entitled") : !string.IsNullOrEmpty(price) ? Strings.F("likeness.price", price) : Strings.T("likeness.noPrice");
            // Authoritative ownership list makes a lost create response/resume recoverable across app restarts.
            var jobs = await _api.Jobs(Token);
            if (!Alive) return;
            var mine = (jobs["jobs"] as JArray)?.OfType<JObject>().Where(x => x.Value<string>("modelId") == _modelId)
                .OrderByDescending(x => x.Value<string>("createdAt"), StringComparer.Ordinal).ToList();
            var selected = mine?.FirstOrDefault(x => x.Value<string>("id") == _jobId) ?? mine?.FirstOrDefault(x => !Terminal(x));
            if (selected == null && string.IsNullOrEmpty(_jobId)) selected = mine?.FirstOrDefault(x => x.Value<string>("id") == jobs.Value<string>("acceptedJobId")) ?? mine?.FirstOrDefault(x => x.Value<string>("status") == "succeeded");
            if (selected != null)
            {
                _jobId = selected.Value<string>("id"); SaveIntent(); await ShowJob(selected);
                if (Alive && !Terminal(selected) && selected.Value<string>("phase") != "awaiting_reference_approval") Watch();
            }
            else
            {
                _jobId = null; _status.text = _enabled && !string.IsNullOrEmpty(_entitlementId) ? "" : Strings.T("likeness.unavailable");
                ShowForm(); RefreshStart();
                // The server can recover a finalized transfer even if the app died before storing its signature.
                // This is owner-wide: another identity's unresolved checkout must also block a second payment.
                var current = (await _api.CurrentCheckout(Token))["checkout"] as JObject;
                if (!Alive) return;
                if (current != null)
                {
                    string id = current.Value<string>("intentId");
                    if (_checkoutId != id) _signature = null;
                    _checkoutId = LikenessClient.Id(id); _signAttempted = true; _checkout = null; SaveIntent();
                    await CheckPayment(current);
                }
                else if (!string.IsNullOrEmpty(_checkoutId)) await CheckPayment();
            }
        }

        private async Task ReviewPayment()
        {
            if (!_checkoutAvailable || _signAttempted || !Services.TryGet<ITransactionSigner>(out _)) return;
            _operationKey ??= Guid.NewGuid().ToString(); SaveIntent();
            var quote = await _api.Checkout(_modelId, IntentKey("checkout"), Token);
            if (!Alive) return;
            _checkout = LikenessCheckout.Parse(quote, _modelId, Env.Current.cluster, DateTimeOffset.UtcNow);
            _checkoutId = _checkout.IntentId; SaveIntent();
            _title.text = Strings.T("likeness.paymentTitle");
            _body.text = Strings.T("likeness.paymentBody");
            _price.text = Strings.F(_checkout.Cluster == "devnet" ? "likeness.paymentDevnet" : "likeness.paymentPrice", _checkout.Amount);
            Display(_form, false); Display(_choose, false); _status.text = "";
            Primary("likeness.pay", () => Run(ConfirmPayment));
            _primary.text = Strings.F("likeness.payAmount", _checkout.Amount);
        }
        private async Task ConfirmPayment()
        {
            if (_checkout == null || _signAttempted || !Services.TryGet<ITransactionSigner>(out var signer)) return;
            if (DateTimeOffset.Parse(_checkout.ExpiresAt) <= DateTimeOffset.UtcNow.AddSeconds(20))
            { _status.text = Strings.T("likeness.paymentExpired"); _primary.SetEnabled(false); Display(_retry, true); return; }
            _signAttempted = true; SaveIntent();
            _status.text = Strings.T("likeness.walletConfirm");
            try { _signature = await signer.SignAndSendBase64(_checkout.Transaction, Token); }
            catch (WalletException e) when (e.UserDeclined)
            { _signAttempted = false; SaveIntent(); if (Alive) { _status.text = Strings.T("likeness.paymentDeclined"); _primary.SetEnabled(true); } return; }
            catch (Exception) { /* Wallet may have broadcast. Only receipt reconciliation is allowed now. */ }
            SaveIntent();
            if (!Alive) return;
            await CheckPayment();
        }
        private async Task CheckPayment(JObject recovered = null)
        {
            if (string.IsNullOrEmpty(_checkoutId)) return;
            Display(_primary, false); _status.text = Strings.T("likeness.paymentPending");
            JObject receipt = recovered ?? (!string.IsNullOrEmpty(_signature)
                ? await _api.SubmitReceipt(_checkoutId, _signature, IntentKey("receipt"), Token)
                : await _api.CheckoutStatus(_checkoutId, Token));
            if (!Alive) return;
            for (int i = 0; i < 5 && receipt.Value<string>("status") == "pending"; i++)
            { await Task.Delay(3000, Token); receipt = await _api.CheckoutStatus(_checkoutId, Token); if (!Alive) return; }
            if (receipt.Value<string>("resolution") == "delivery_owed")
            {
                _status.text = Strings.T("likeness.failed"); Display(_retry, true);
            }
            else if (receipt.Value<string>("status") == "expired")
            {
                // Server expiration requires finalized block-height expiry AND a negative finalized reference scan.
                // A missing current intent or a wall-clock timeout alone is insufficient to clear this guard.
                _checkout = null; _signAttempted = false; _checkoutId = null; _signature = null;
                _operationKey = Guid.NewGuid().ToString(); SaveIntent(); ShowForm();
                _status.text = Strings.T("likeness.paymentExpiredUnpaid");
            }
            else if (receipt.Value<string>("status") == "confirmed" && !string.IsNullOrEmpty(receipt.Value<string>("entitlementId")) && receipt.Value<string>("modelId") == _modelId)
            {
                _entitlementId = receipt.Value<string>("entitlementId");
                _checkout = null; _signAttempted = false; _checkoutId = null; _signature = null; SaveIntent();
                _price.text = Strings.T("likeness.paymentConfirmed"); ShowForm();
                if (_photoBytes != null && _own.value && _provider.value) await StartCreation();
            }
            else
            {
                _status.text = Strings.T(receipt.Value<string>("status") == "expired" ? "likeness.paymentExpired" : "likeness.paymentPending");
                Display(_retry, true);
            }
        }

        private async Task PickPhoto()
        {
            byte[] photo;
            try { photo = await LikenessPhotoPicker.Pick(Token); }
            catch (InvalidDataException) { if (Alive) _status.text = Strings.T("likeness.badPhoto"); return; }
            if (!Alive || photo == null) return;
            if (!string.IsNullOrEmpty(_uploadId)) { await _api.DeletePhoto(_uploadId, Token); _uploadId = null; }
            try
            {
                LikenessImageHeader.Size(photo, 0, photo.Length, out var width, out var height);
                if (photo.Length > LikenessClient.MaxPhotoBytes || width > 4096 || height > 4096) throw new InvalidDataException();
                SetImage(photo);
            }
            catch (InvalidDataException) { _status.text = Strings.T("likeness.badPhoto"); return; }
            _photoBytes = photo; _choose.text = Strings.T("likeness.change"); _status.text = "";
            _operationKey = Guid.NewGuid().ToString(); _uploadId = null; SaveIntent();
        }
        private void RefreshStart()
        {
            if (_root == null || !string.IsNullOrEmpty(_jobId)) return;
            if (_checkout != null || _signAttempted) return;
            bool canPay = _checkoutAvailable && Services.TryGet<IWalletSignIn>(out var wallet) && wallet.IsAvailable && Services.TryGet<ITransactionSigner>(out _);
            _primary.text = Strings.T(string.IsNullOrEmpty(_entitlementId) && canPay ? "likeness.reviewPayment" : "likeness.start");
            _primary.SetEnabled(!_busy && _enabled && _photoBytes != null && _own.value && _provider.value && (!string.IsNullOrEmpty(_entitlementId) || canPay));
        }
        private async Task StartCreation()
        {
            if (!_enabled || _photoBytes == null || !_own.value || !_provider.value) return;
            if (string.IsNullOrEmpty(_entitlementId)) { await ReviewPayment(); return; }
            _operationKey ??= Guid.NewGuid().ToString(); SaveIntent();
            _status.text = Strings.T("likeness.uploading");
            if (string.IsNullOrEmpty(_uploadId))
            {
                var uploaded = await _api.Upload(_photoBytes, _photoBytes[0] == 137 ? "image/png" : "image/jpeg", _operationKey, Token);
                if (!Alive) return;
                _uploadId = uploaded.Value<string>("uploadId"); SaveIntent();
            }
            // Distinct retained action key; a lost response replays the same intent, never a new paid job.
            string createKey = IntentKey("create");
            var job = await _api.Create(_modelId, _uploadId, _entitlementId, createKey, Token);
            if (!Alive) return;
            _jobId = job.Value<string>("id"); SaveIntent(); _photoBytes = null;
            await ShowJob(job); Watch();
        }
        private async void Watch()
        {
            if (_watching || !Alive || string.IsNullOrEmpty(_jobId)) return;
            _watching = true;
            try
            {
                while (Alive)
                {
                    await Task.Delay(5000, Token);
                    if (_busy) continue;
                    var job = await _api.Job(_jobId, Token);
                    if (!Alive) break;
                    await ShowJob(job);
                    if (Terminal(job) || job.Value<string>("phase") == "awaiting_reference_approval") break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (Alive) { _status.text = Strings.T("likeness.offline"); Display(_retry, true); } }
            finally { _watching = false; }
        }
        private async Task ShowJob(JObject job)
        {
            if (!Alive) return;
            var phase = job.Value<string>("phase"); var status = job.Value<string>("status");
            bool evaluation = job.Value<string>("source") == "operator_evaluation";
            Display(_price, evaluation);
            if (evaluation) _price.text = Strings.T("likeness.syntheticDemo");
            Display(_form, false); Display(_choose, false); Display(_primary, false); Display(_copyReference, false); Display(_cancel, !Terminal(job)); Display(_deletePhoto, !string.IsNullOrEmpty(_uploadId));
            _status.text = Strings.T("likeness.working");
            if (status == "failed" || status == "cancelled" || phase == "reconciliation_required")
            {
                _title.text = Strings.T("likeness.title");
                _status.text = Strings.T(status == "cancelled" ? "likeness.cancelled" : "likeness.failed");
                Display(_retry, true); Display(_copyReference, true);
                // A retained failed job must still reconcile its payment. It may remain delivery-owed;
                // checking its receipt does not reopen checkout or spend its entitlement again.
                var current = (await _api.CurrentCheckout(Token))["checkout"] as JObject;
                if (!Alive) return;
                if (current != null)
                {
                    string id = LikenessClient.Id(current.Value<string>("intentId"));
                    if (_checkoutId != id) _signature = null;
                    _checkoutId = id; _signAttempted = true; _checkout = null; SaveIntent();
                }
                return;
            }
            if (phase == "awaiting_reference_approval")
            {
                var expected = job.Value<string>("referenceSha256");
                if (_referenceHash != expected)
                {
                    var reference = await _api.Reference(_jobId, Token);
                    if (!Alive) return;
                    if (string.IsNullOrEmpty(expected) || LikenessClient.Sha256(reference.bytes) != expected) throw new InvalidDataException("Reference differs from approval hash");
                    SetImage(reference.bytes); _referenceHash = expected;
                }
                _title.text = Strings.T("likeness.referenceTitle"); _body.text = Strings.T("likeness.referenceBody"); _status.text = "";
                Primary("likeness.approveReference", () => Run(ApproveReference));
            }
            else if (status == "succeeded" && phase == "completed")
            {
                if (_manifest?.jobId != _jobId)
                {
                    var manifest = await _api.Manifest(_jobId, Token);
                    var model = await _api.Model(_jobId, Token);
                    if (!Alive) return;
                    await PresentModel(model.bytes, manifest, true);
                    _manifest = manifest;
                }
                _title.text = Strings.T("likeness.previewTitle"); _body.text = Strings.T("likeness.previewBody"); _status.text = "";
                Primary("likeness.accept", () => Run(Accept));
            }
            else if (phase == "awaiting_asset_validation")
            {
                if (_previewJob != _jobId)
                {
                    var preview = await _api.Preview(_jobId, Token);
                    if (!Alive) return;
                    var manifest = new LikenessAssetManifest { jobId = _jobId, modelId = _modelId, bytes = preview.bytes.Length,
                        sha256 = LikenessClient.Sha256(preview.bytes), heightMeters = 1.75f };
                    await PresentModel(preview.bytes, manifest, false); _previewJob = _jobId;
                }
                _title.text = Strings.T("likeness.previewTitle"); _body.text = Strings.T("likeness.previewBody"); _status.text = Strings.T("likeness.validating");
            }
            else _title.text = Strings.T(phase != null && phase.Contains("rig") ? "likeness.processing.rig" : phase != null && phase.Contains("mesh") ? "likeness.processing.mesh" : "likeness.processing.reference");
        }
        private async Task ApproveReference()
        {
            if (string.IsNullOrEmpty(_referenceHash)) return;
            await _api.Approve(_jobId, _referenceHash, IntentKey("approve-" + _referenceHash), Token);
            if (!Alive) return;
            Display(_primary, false); _status.text = Strings.T("likeness.working"); Watch();
        }
        private async Task PresentModel(byte[] bytes, LikenessAssetManifest manifest, bool playable)
        {
            _modelPreview ??= new LikenessModelPreview();
            var asset = await LikenessAvatarLoader.Load(bytes, manifest, _modelPreview.Parent, playable, Token);
            if (!Alive) { asset.Dispose(); return; }
            _asset?.Dispose(); _asset = asset; _modelBytes = bytes;
            _picture.Clear(); _picture.style.backgroundImage = Background.FromRenderTexture(_modelPreview.Texture);
            _modelPreview.Present(asset);
        }
        private async Task Accept()
        {
            if (_manifest == null || _asset?.CanEquip != true) return;
            _status.text = Strings.T("likeness.accepting");
            var receipt = await _api.Accept(_jobId, IntentKey("accept"), Token);
            if (!Alive) return;
            var accepted = receipt.ToObject<LikenessAssetManifest>();
            if (accepted?.jobId != _manifest.jobId || accepted.sha256 != _manifest.sha256) throw new InvalidDataException("Accepted character differs from displayed result");
            await LikenessAvatarCache.PrepareAccepted(accepted, _modelBytes, Token);
            if (!Alive) return;
            Accepted?.Invoke(accepted); Close();
        }
        private async Task CancelCreation()
        {
            if (string.IsNullOrEmpty(_jobId)) return;
            await _api.Cancel(_jobId, Token);
            if (Alive) { _status.text = Strings.T("likeness.cancelled"); Display(_primary, false); Display(_cancel, false); Display(_retry, true); Display(_copyReference, true); }
        }
        private async Task DeletePhoto()
        {
            if (string.IsNullOrEmpty(_uploadId)) return;
            await _api.DeletePhoto(_uploadId, Token);
            if (Alive) { _uploadId = null; SaveIntent(); _status.text = Strings.T("likeness.photoDeleted"); Display(_primary, false); Display(_deletePhoto, false); Display(_cancel, false); }
        }
        private void SetImage(byte[] data)
        {
            LikenessImageHeader.Size(data, 0, data.Length, out int width, out int height);
            if (width > 4096 || height > 4096) throw new InvalidDataException("Photo dimensions exceed display budget");
            var next = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!next.LoadImage(data, true)) { UnityEngine.Object.Destroy(next); throw new InvalidDataException("Photo decode failed"); }
            if (_photo != null) UnityEngine.Object.Destroy(_photo);
            _photo = next; _picture.Clear(); _picture.style.backgroundImage = new StyleBackground(next);
        }
        private void ShowForm()
        { _title.text = Strings.T("likeness.title"); _body.text = Strings.T("likeness.intro"); Display(_price, true); Display(_form, true); Display(_choose, true); Display(_cancel, false); Display(_copyReference, false); Display(_deletePhoto, !string.IsNullOrEmpty(_uploadId)); Primary("likeness.start", () => Run(StartCreation)); }
        private void Primary(string key, Action action) { _primary.text = Strings.T(key); _primaryAction = action; Display(_primary, true); _primary.SetEnabled(true); }
        private static bool Terminal(JObject job) => new[] { "succeeded", "failed", "cancelled" }.Contains(job.Value<string>("status")) || job.Value<string>("phase") == "reconciliation_required";
        private static Label Text(string key, string css) => El.Text(Strings.T(key), css);
        private static Button Button(string key, Action action, string css)
        { var b = new Button(action) { text = Strings.T(key), enableRichText = false }; b.AddToClassList("lk-button"); b.AddToClassList(css); return b; }
        private static void Display(VisualElement e, bool show) { if (e != null) e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None; }
        private string IntentKey(string action)
        {
            var key = StoreKey + "." + (_jobId ?? _operationKey) + "." + action;
            var value = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(value)) { value = Guid.NewGuid().ToString(); PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); }
            return value;
        }
        private void SaveIntent() { PlayerPrefs.SetString(StoreKey, new JObject { ["jobId"] = _jobId, ["uploadId"] = _uploadId, ["operationKey"] = _operationKey, ["checkoutId"] = _checkoutId, ["signature"] = _signature, ["signAttempted"] = _signAttempted }.ToString()); PlayerPrefs.Save(); }
        private void RestoreIntent()
        { try { var j = JObject.Parse(PlayerPrefs.GetString(StoreKey, "{}")); _jobId = j.Value<string>("jobId"); _uploadId = j.Value<string>("uploadId"); _operationKey = j.Value<string>("operationKey"); _checkoutId = j.Value<string>("checkoutId"); _signature = j.Value<string>("signature"); _signAttempted = j.Value<bool?>("signAttempted") == true; } catch { } }
        private void SessionChanged() { if (_session.UserId != _owner || !_session.IsSignedIn) Close(); }
        public void Close()
        {
            bool wasOpen = _root != null; _closed = true;
            if (_session != null) _session.Changed -= SessionChanged;
            _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
            _root?.RemoveFromHierarchy(); _root = null;
            _photoBytes = null; _modelBytes = null;
            if (_photo != null) { UnityEngine.Object.Destroy(_photo); _photo = null; }
            _asset?.Dispose(); _asset = null; _modelPreview?.Dispose(); _modelPreview = null;
            if (wasOpen) Closed?.Invoke();
        }
    }
}
