using System;
using System.Linq;
using CTLife.World;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// An explicitly supplied local base and its real art. Does not register Look IDs or grant submission.
    /// The caller supplies reviewed metadata; the screen adds it alongside, not instead of, the shipped roster.
    /// </summary>
    public sealed class CreatorLocalArtBinding
    {
        public readonly ExactModularCatalog Art;
        public readonly CustomizerCatalog Options;
        public readonly BaseModel Model;
        private readonly AvatarSpec _default;

        public CreatorLocalArtBinding(ExactModularCatalog art, CustomizerCatalog options, AvatarSpec defaultSpec,
            string label, char family, float previewYaw, Texture2D portrait = null)
        {
            if (art == null || options == null || defaultSpec == null || string.IsNullOrWhiteSpace(label) ||
                defaultSpec.baseId != art.baseId || defaultSpec.legacyLook != null || !defaultSpec.IsModular)
                throw new ArgumentException("Local art needs its exact catalogue, local default spec and display metadata.");
            var data = options.bases.Where(b => b != null && b.id == art.baseId).ToArray();
            if (data.Length != 1 || !ExactCustomizerSelection.Resolves(defaultSpec, art))
                throw new ArgumentException("Local art default is incomplete or its option base is ambiguous.");
            Art = art; Options = options; _default = defaultSpec.Clone();
            Model = new BaseModel { id = art.baseId, label = label, family = family, previewYaw = previewYaw,
                portrait = portrait, data = data[0], look = null };
        }

        public AvatarSpec DefaultSpec() => _default.Clone();
        public bool Resolves(AvatarSpec spec) => spec != null && spec.baseId == Model.id &&
            spec.legacyLook == null && ExactCustomizerSelection.Resolves(spec, Art);
    }
}
