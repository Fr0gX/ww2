using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace WW2.Runtime
{
    /// <summary>Command-line-only visual regression capture; inert in ordinary play.</summary>
    public sealed class RuntimeVisualCapture : MonoBehaviour
    {
        private IEnumerator Start()
        {
            var arguments = Environment.GetCommandLineArgs();
            var path = string.Empty;
            var focus = default(Vector3?);
            var viewSize = 4.8f;
            var previewWallStages = false;
            var captureHud = false;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], "-captureScreenshot", StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < arguments.Length)
                    path = arguments[i + 1];
                else if (string.Equals(arguments[i], "-captureFocus", StringComparison.OrdinalIgnoreCase) &&
                         i + 3 < arguments.Length && int.TryParse(arguments[i + 1], out var q) &&
                         int.TryParse(arguments[i + 2], out var r) &&
                         float.TryParse(arguments[i + 3], NumberStyles.Float, CultureInfo.InvariantCulture,
                             out var parsedSize))
                {
                    focus = HexMapView.ToWorld(new WW2.Core.Model.HexCoord(q, r));
                    viewSize = Mathf.Clamp(parsedSize, 2.6f, 12f);
                }
                else if (string.Equals(arguments[i], "-previewWallStages", StringComparison.OrdinalIgnoreCase))
                    previewWallStages = true;
                else if (string.Equals(arguments[i], "-captureHud", StringComparison.OrdinalIgnoreCase))
                    captureHud = true;
            }
            if (string.IsNullOrWhiteSpace(path)) yield break;
            yield return new WaitForSecondsRealtime(0.85f);

            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var camera = Camera.main;
            if (camera == null) { Application.Quit(2); yield break; }
            var mapView = FindFirstObjectByType<HexMapView>();
            if (previewWallStages && mapView != null) mapView.ApplyDiagnosticWallStages();
            if (focus.HasValue)
            {
                camera.transform.rotation = WorldPresentation.CameraRotation;
                camera.transform.position = WorldPresentation.CameraPositionForTarget(focus.Value, viewSize, 1.0f);
                camera.fieldOfView = WorldPresentation.CameraFieldOfView;
            }

            if (captureHud)
            {
                yield return new WaitForEndOfFrame();
                var captureType = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
                var captureMethod = captureType?.GetMethod("CaptureScreenshotAsTexture", new[] { typeof(int) });
                if (captureMethod == null)
                {
                    Application.Quit(4);
                    yield break;
                }
                var screenTexture = captureMethod.Invoke(null, new object[] { 1 }) as Texture2D;
                if (screenTexture == null)
                {
                    Application.Quit(5);
                    yield break;
                }
                WritePortablePixmap(path, screenTexture.GetPixels32(), screenTexture.width, screenTexture.height);
                Destroy(screenTexture);
                Application.Quit(0);
                yield break;
            }

            const int width = 2560;
            const int height = 1440;
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = 8
            };
            var target = new RenderTexture(descriptor);
            target.Create();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            var resolved = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(target, resolved);
            RenderTexture.active = resolved;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            texture.Apply(false, false);
            WritePortablePixmap(path, texture.GetPixels32(), width, height);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(resolved);
            target.Release();
            Destroy(target);
            Destroy(texture);
            Application.Quit(0);
        }

        private static void WritePortablePixmap(string path, Color32[] pixels, int width, int height)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
            stream.Write(header, 0, header.Length);
            var row = new byte[width * 3];
            for (var y = height - 1; y >= 0; y--)
            {
                var start = y * width;
                for (var x = 0; x < width; x++)
                {
                    var color = pixels[start + x];
                    var offset = x * 3;
                    row[offset] = color.r;
                    row[offset + 1] = color.g;
                    row[offset + 2] = color.b;
                }
                stream.Write(row, 0, row.Length);
            }
        }
    }
}
