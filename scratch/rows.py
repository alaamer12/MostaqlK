import sys
from PIL import Image
import numpy as np

path, x0, x1, y0, y1 = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
a = np.asarray(Image.open(path).convert("L")).astype(float)
seg = a[y0:y1, x0:x1]
for i, row in enumerate(seg):
    print(y0 + i, round(float(row.mean()), 2), int(row.min()))
