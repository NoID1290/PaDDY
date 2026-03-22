using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

// Generate multi-size ICO with microphone icon
var sizes = new[] { 16, 32, 48, 256 };
using var ms = new MemoryStream();
using var bw = new BinaryWriter(ms);

// ICO header
bw.Write((short)0);  // reserved
bw.Write((short)1);  // type: icon
bw.Write((short)sizes.Length);

// Prepare all images first
var images = new List<byte[]>();
foreach (int sz in sizes)
{
    using var bmp = new Bitmap(sz, sz, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.Clear(Color.Transparent);

    float pad = sz * 0.1f;
    float w = sz - pad * 2;
    float cx = sz / 2f;

    // Green circle background
    using var bgBrush = new SolidBrush(Color.FromArgb(76, 175, 80)); // #4CAF50
    g.FillEllipse(bgBrush, pad, pad, w, w);

    // Dark circle inner shadow
    using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
    g.FillEllipse(shadowBrush, pad + 1, pad + 1, w - 2, w - 2);

    // Microphone head (rounded rect / ellipse)
    float micW = w * 0.22f;
    float micH = w * 0.32f;
    float micX = cx - micW / 2;
    float micY = sz * 0.2f;

    using var whiteBrush = new SolidBrush(Color.White);
    using var whitePen = new Pen(Color.White, Math.Max(1.2f, sz * 0.04f));
    whitePen.StartCap = LineCap.Round;
    whitePen.EndCap = LineCap.Round;

    // Mic capsule (rounded rect approximation)
    var capsule = new RectangleF(micX, micY, micW, micH);
    using var capsulePath = new GraphicsPath();
    float r = micW / 2;
    capsulePath.AddArc(capsule.X, capsule.Y, micW, micW, 180, 180);
    capsulePath.AddLine(capsule.Right, capsule.Y + r, capsule.Right, capsule.Bottom - r);
    capsulePath.AddArc(capsule.X, capsule.Bottom - micW, micW, micW, 0, 180);
    capsulePath.CloseFigure();
    g.FillPath(whiteBrush, capsulePath);

    // Arc below capsule (mic holder)
    float arcW = micW * 1.6f;
    float arcH = micH * 0.5f;
    float arcX = cx - arcW / 2;
    float arcY = capsule.Bottom - arcH * 0.2f;
    g.DrawArc(whitePen, arcX, arcY, arcW, arcH, 0, 180);

    // Stem
    float stemTop = arcY + arcH / 2 + arcH * 0.35f;
    float stemBot = stemTop + w * 0.12f;
    g.DrawLine(whitePen, cx, stemTop, cx, stemBot);

    // Base
    float baseW = micW * 1.2f;
    g.DrawLine(whitePen, cx - baseW / 2, stemBot, cx + baseW / 2, stemBot);

    // Save as PNG to byte array
    using var pngMs = new MemoryStream();
    bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
    images.Add(pngMs.ToArray());
}

// Write directory entries
int dataOffset = 6 + sizes.Length * 16; // header + entries
for (int i = 0; i < sizes.Length; i++)
{
    byte dim = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
    bw.Write(dim);        // width
    bw.Write(dim);        // height
    bw.Write((byte)0);    // colors
    bw.Write((byte)0);    // reserved
    bw.Write((short)1);   // color planes
    bw.Write((short)32);  // bits per pixel
    bw.Write(images[i].Length);  // size
    bw.Write(dataOffset);        // offset
    dataOffset += images[i].Length;
}

// Write image data
foreach (var img in images)
    bw.Write(img);

File.WriteAllBytes(@"s:\VScodeProjects\Paddy-dev\Paddy.ico", ms.ToArray());
Console.WriteLine("ICO created successfully: " + string.Join(", ", sizes.Select(s => $"{s}x{s}")));
