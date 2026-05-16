from PIL import Image, ImageDraw, ImageFont

def create_wordmark(text, font_path, scale=1):
    base_font_size = 120
    font_size = base_font_size * scale
    try:
        font = ImageFont.truetype(font_path, font_size)
    except Exception as e:
        print(f"Error loading font: {e}")
        return None

    # Get text dimensions using getmask2
    mask, offset = font.getmask2(text, "L")
    width, height = mask.size
    
    # Create image with transparent background (RGBA)
    # We add a bit of padding
    padding = 10 * scale
    img_width = width + 2 * padding
    img_height = height + 2 * padding
    
    img = Image.new("RGBA", (img_width, img_height), (0, 0, 0, 0))
    
    # Create gradient mask
    # Clean blue-to-green horizontal gradient
    # Blue: (0, 120, 215), Green: (0, 255, 128) - sample values
    draw = ImageDraw.Draw(img)
    
    # We want to fill the text specifically.
    # We'll create a gradient image of the same size and use the text mask.
    gradient = Image.new("RGB", (img_width, img_height))
    for x in range(img_width):
        r = 0
        g = int(120 + (135 * x / img_width))
        b = int(215 - (87 * x / img_width))
        for y in range(img_height):
            gradient.putpixel((x, y), (r, g, b))
    
    # Use the mask to paste the gradient onto the transparent image
    # Note: font.getmask produces a 1-bit or L mask.
    text_mask = Image.new("L", (img_width, img_height), 0)
    # Re-draw text on the mask or paste the mask retrieved earlier
    text_mask_canvas = ImageDraw.Draw(text_mask)
    text_mask_canvas.text((padding - offset[0], padding - offset[1]), text, font=font, fill=255)
    
    img.paste(gradient, (0, 0), mask=text_mask)
    
    return img

font_p = "Themes/Fonts/ari-w9500-display.ttf"
text = "PaDDY"

# Generate 1x
img1 = create_wordmark(text, font_p, scale=2) # Starting with a larger base for "large for GitHub"
if img1:
    path1 = "logo/github/PaDDY-wordmark-font-transparent.png"
    img1.save(path1)
    print(f"Saved 1x: {path1} {img1.size}")
    
    # Generate 2x using nearest-neighbor for crisp pixel feel
    w, h = img1.size
    img2 = img1.resize((w*2, h*2), resample=Image.NEAREST)
    path2 = "logo/github/PaDDY-wordmark-font-transparent-2x.png"
    img2.save(path2)
    print(f"Saved 2x: {path2} {img2.size}")

