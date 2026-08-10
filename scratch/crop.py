import sys
from PIL import Image
box = tuple(int(v) for v in sys.argv[3:7])
Image.open(sys.argv[1]).crop(box).save(sys.argv[2])
