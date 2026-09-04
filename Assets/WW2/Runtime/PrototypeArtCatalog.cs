using System;
using UnityEngine;

namespace WW2.Runtime
{
    /// <summary>Loads the small set of retained runtime battle-effect textures.</summary>
    public static class PrototypeArtCatalog
    {
        public static Texture2D Texture(string relativePath)
        {
            var texture = Resources.Load<Texture2D>("Art/" + relativePath);
            if (texture == null) throw new InvalidOperationException($"Missing art texture: {relativePath}");
            return texture;
        }
    }
}
