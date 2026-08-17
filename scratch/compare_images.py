import sys
from PIL import Image
import numpy as np
from skimage.metrics import structural_similarity as ssim

def crop_titlebar(img, top_px=32):
    w, h = img.size
    return img.crop((0, top_px, w, h))

def main(current_path, design_path, out_diff_path, top_crop=32):
    cur = Image.open(current_path).convert("RGB")
    des = Image.open(design_path).convert("RGB")

    cur = crop_titlebar(cur, top_crop)

    # Resize current to match design size
    cur = cur.resize(des.size, Image.LANCZOS)

    cur_arr = np.array(cur)
    des_arr = np.array(des)

    score, diff = ssim(cur_arr, des_arr, channel_axis=2, full=True)
    diff_img = (1 - diff) * 255
    diff_img = diff_img.astype(np.uint8)
    Image.fromarray(diff_img).save(out_diff_path)

    print(f"SSIM similarity: {score*100:.2f}%")

if __name__ == "__main__":
    current_path = sys.argv[1]
    design_path = sys.argv[2]
    out_diff_path = sys.argv[3]
    top_crop = int(sys.argv[4]) if len(sys.argv) > 4 else 32
    main(current_path, design_path, out_diff_path, top_crop)
