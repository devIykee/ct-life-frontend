using System.Threading;
using System.Threading.Tasks;

namespace CTLife.Ventures
{
    public sealed class PickedImage
    {
        public string Name;
        public byte[] Bytes;
    }

    /// <summary>
    /// Lets the player choose an image from the device (Android document picker). Not part of this lane: the composition root
    /// registers one when a vetted picker exists (hook in docs/progress/requests.md). Without it the Ad Desk reads a file from the
    /// game's own data folder instead.
    /// </summary>
    public interface ICreativePicker
    {
        /// <summary>Null when the player cancelled.</summary>
        Task<PickedImage> Pick(CancellationToken ct);
    }
}
