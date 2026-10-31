using System;

namespace Hansa
{
    /// <summary>The active fire fixture remains the packet target even though its firebed differs from its furniture box.</summary>
    public static class FirepitTarget
    {
        public static FixtureDef ForPiece(ShipPiece piece, FixtureDef hearth, FixtureDef oven)
        {
            if (piece == null) return null;
            var fire = piece.Role == PieceRole.Hearth ? hearth : piece.Role == PieceRole.Oven ? oven : null;
            return fire != null && fire.Code == piece.StorageCode && fire.Box != null && piece.Box != null &&
                fire.Box.AsSpan().SequenceEqual(piece.Box) ? fire : null;
        }

        /// <summary>Without a chimney the oven smokes at its fire, as the hearth does.</summary>
        public static double[] SmokeOutlet(FixtureDef fire) => fire.Chimney ?? fire.Fire ?? fire.Pos;
    }
}
