import glob,sys
from PIL import Image
pre=sys.argv[1]
fs=sorted(glob.glob(pre+'_*.png'), key=lambda f:(not f.endswith('_Rest.png'), f)); ims=[Image.open(f) for f in fs]
s=Image.new('RGB',(sum(i.width for i in ims),ims[0].height),'white'); x=0
for i in ims: s.paste(i,(x,0)); x+=i.width
s.save(sys.argv[2]); print([f.split('_')[-1] for f in fs])
