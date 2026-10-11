using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Everything `POST /v1/characters` needs (contract section 4).</summary>
    public sealed class CharacterDraft
    {
        public string alias = "";
        public readonly string[] look = new string[7]; // [skinTone, headId, hairId, topId, bottomId, shoesId, accId]
        public string tribeId;
        public readonly List<string> traitIds = new List<string>();
        public string dreamId;
        public bool ageConfirmed;
    }

    /// <summary>
    /// Character creator (feature #2): modular look from starter items in items.json, tribe pick (perks from
    /// tribes.json), two traits, one dream, alias, and the 18+ confirmation. 3D preview via AvatarPreview.
    /// </summary>
    public sealed class CharacterCreatorScreen : UiScreen
    {
        private static readonly string[] SlotOrder = { "skin", "head", "hair", "top", "bottom", "shoes", "acc" };
        private static readonly string[] SlotLabels = { "Skin", "Face", "Hair", "Top", "Bottom", "Shoes", "Extra" };

        private readonly Registries _reg;
        private readonly Action<CharacterDraft> _onSubmit;
        private readonly CharacterDraft _draft = new CharacterDraft();
        private readonly Dictionary<string, List<ItemDef>> _bySlot = new Dictionary<string, List<ItemDef>>();
        private AvatarPreview _preview;
        private Label _error;
        private Button _submit;
        private VisualElement _tribeRow, _traitRow, _dreamRow;

        public CharacterCreatorScreen(Registries reg, Action<CharacterDraft> onSubmit)
        {
            _reg = reg;
            _onSubmit = onSubmit;
            foreach (var slot in SlotOrder)
            {
                _bySlot[slot] = reg.Items.Values.Where(i => i.slot == slot && i.starter).OrderBy(i => i.id).ToList();
                // contract-v1.0.0-rc1 items.json has no skin items, one head and no "none" accessory, but Look requires
                // all seven slots. FE fallbacks keep the creator usable; [Unverified] that the server accepts them
                // (raised in docs/progress/backend-dev-feedback.md).
                if (_bySlot[slot].Count == 0 || slot == "acc" || slot == "head")
                    foreach (var (id, name) in FallbackItems(slot))
                        if (_bySlot[slot].All(i => i.id != id))
                            _bySlot[slot].Insert(0, new ItemDef { id = id, slot = slot, name = name, starter = true });
            }
            for (int s = 0; s < SlotOrder.Length; s++)
            {
                var list = _bySlot[SlotOrder[s]];
                _draft.look[s] = list.Count > 0 ? list[UnityEngine.Random.Range(0, list.Count)].id : null;
            }
            if (_bySlot["acc"].Any(i => i.id == "acc_none")) _draft.look[6] = "acc_none";
        }

        protected override VisualElement Build()
        {
            _preview = new AvatarPreview();
            var previewEl = El.Div("ct-preview");
            previewEl.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            previewEl.RegisterCallback<PointerMoveEvent>(e => { if (e.pressedButtons != 0) _preview.Spin(e.deltaPosition.x); });
            _preview.SetLook(_draft.look);

            var slots = El.Div("ct-col");
            for (int s = 0; s < SlotOrder.Length; s++) slots.Add(SlotPicker(s));

            var alias = new TextField("Alias") { maxLength = 20 };
            alias.AddToClassList("ct-field");
            alias.RegisterValueChangedCallback(e => { _draft.alias = e.newValue.Trim(); Validate(); });

            _tribeRow = El.Div("ct-chips");
            foreach (var t in _reg.TribeOrder) _tribeRow.Add(Chip(t.name, () => { _draft.tribeId = t.id; Refresh(); }, t.id, "tribe", PerkText(t)));
            _traitRow = El.Div("ct-chips");
            foreach (var t in _reg.Traits) _traitRow.Add(Chip(t.name, () => ToggleTrait(t.id), t.id, "trait"));
            _dreamRow = El.Div("ct-chips");
            foreach (var d in _reg.Dreams) _dreamRow.Add(Chip(d.name, () => { _draft.dreamId = d.id; Refresh(); }, d.id, "dream"));

            var age = new Toggle("I am 18 or older");
            age.AddToClassList("ct-toggle");
            age.RegisterValueChangedCallback(e => { _draft.ageConfirmed = e.newValue; Validate(); });

            _error = El.Text("", "ct-error");
            _submit = El.Btn("Be born", () => _onSubmit?.Invoke(_draft), "ct-btn ct-btn-primary ct-btn-big");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ct-scroll");
            scroll.Add(El.Text("Who are you on CT?", "ct-h1"));
            scroll.Add(alias);
            scroll.Add(El.Text("Look", "ct-h2"));
            scroll.Add(slots);
            scroll.Add(El.Text("Tribe", "ct-h2"));
            scroll.Add(_tribeRow);
            scroll.Add(El.Text("Two traits", "ct-h2"));
            scroll.Add(_traitRow);
            scroll.Add(El.Text("Dream", "ct-h2"));
            scroll.Add(_dreamRow);
            scroll.Add(age);
            scroll.Add(_error);
            scroll.Add(_submit);

            var root = El.Div("ct-creator", previewEl, scroll);
            Refresh();
            return root;
        }

        /// <summary>FE defaults matching the art kit (art/processed/characters: skin_tone1..6, male/female base bodies).</summary>
        private static IEnumerable<(string, string)> FallbackItems(string slot)
        {
            switch (slot)
            {
                case "skin":
                    for (int i = 6; i >= 1; i--) yield return ("skin_tone" + i, "Skin tone " + i);
                    break;
                case "head":
                    yield return ("head_female", "Face B");
                    yield return ("head_male", "Face A");
                    break;
                case "acc":
                    yield return ("acc_none", "None");
                    break;
            }
        }

        private static string PerkText(TribeDef t) => t.perkText ?? t.perkKind switch
        {
            "launch_preview_ms" => $"Sees launches {t.perkValue / 1000:0}s early",
            "leverage_bonus_x" => $"Leverage cap +{t.perkValue:0}x",
            "prediction_fee_discount_bps" => $"Prediction fee −{t.perkValue / 100:0}%",
            "farm_yield_bonus_bps" => $"Farm yield +{t.perkValue / 100:0}%",
            "call_reach_multiplier" => $"Calls reach {t.perkValue:0}x followers",
            "project_cost_discount_bps" => $"Launch cost −{t.perkValue / 100:0}%",
            "hidden_from_rich_list" => "Hidden from the rich list",
            _ => t.perkKind,
        };

        private VisualElement SlotPicker(int slotIndex)
        {
            var slot = SlotOrder[slotIndex];
            var label = El.Text(Name(_draft.look[slotIndex]), "ct-slot-value");
            void Step(int d)
            {
                var list = _bySlot[slot];
                if (list.Count == 0) return;
                int i = list.FindIndex(x => x.id == _draft.look[slotIndex]);
                i = ((i < 0 ? 0 : i) + d + list.Count) % list.Count;
                _draft.look[slotIndex] = list[i].id;
                label.text = list[i].name;
                _preview.SetLook(_draft.look);
            }
            return El.Div("ct-slot",
                El.Text(SlotLabels[slotIndex], "ct-slot-label"),
                El.Btn("‹", () => Step(-1), "ct-btn ct-btn-icon", "sfx_ui_toggle"),
                label,
                El.Btn("›", () => Step(1), "ct-btn ct-btn-icon", "sfx_ui_toggle"));
        }

        private string Name(string itemId) => itemId != null && _reg.Items.TryGetValue(itemId, out var d) ? d.name : "—";

        private VisualElement Chip(string text, Action onClick, string id, string group, string sub = null)
        {
            var b = El.Btn(text, onClick, "ct-chip", "sfx_ui_toggle");
            b.userData = group + ":" + id;
            if (sub != null) b.tooltip = sub;
            if (sub != null) b.Add(El.Text(sub, "ct-chip-sub"));
            return b;
        }

        private void ToggleTrait(string id)
        {
            if (_draft.traitIds.Contains(id)) _draft.traitIds.Remove(id);
            else
            {
                if (_draft.traitIds.Count >= 2) _draft.traitIds.RemoveAt(0);
                _draft.traitIds.Add(id);
            }
            Refresh();
        }

        private void Refresh()
        {
            Mark(_tribeRow, "tribe", id => id == _draft.tribeId);
            Mark(_traitRow, "trait", id => _draft.traitIds.Contains(id));
            Mark(_dreamRow, "dream", id => id == _draft.dreamId);
            Validate();
        }

        private static void Mark(VisualElement row, string group, Func<string, bool> selected)
        {
            if (row == null) return;
            foreach (var c in row.Children())
            {
                var tag = c.userData as string;
                if (tag == null || !tag.StartsWith(group + ":")) continue;
                c.EnableInClassList("ct-chip-on", selected(tag.Substring(group.Length + 1)));
            }
        }

        private void Validate()
        {
            if (_submit == null) return;
            string problem = null;
            if (_draft.alias.Length < 3) problem = "Alias needs 3+ characters";
            else if (_draft.tribeId == null) problem = "Pick a tribe";
            else if (_draft.traitIds.Count != 2) problem = "Pick two traits";
            else if (_draft.dreamId == null) problem = "Pick a dream";
            else if (!_draft.ageConfirmed) problem = "Confirm you are 18+";
            _submit.SetEnabled(problem == null);
            _submit.text = problem ?? "Be born";
        }

        public void ShowError(string text)
        {
            if (_error != null) _error.text = text;
        }

        public void SetBusy(bool busy) => _submit?.SetEnabled(!busy);

        public override void Close()
        {
            base.Close();
            _preview?.Dispose();
            _preview = null;
        }
    }
}
