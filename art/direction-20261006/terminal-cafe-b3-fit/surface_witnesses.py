"""Shared read-only vertical surface geometry utilities; no scene or asset mutation."""
import numpy as np

def vertical(points,triangles,lower,upper,highest=False):
    result=np.full(len(points),-np.inf if highest else np.inf)
    for a,b,c in triangles:
        ax,az=a[[0,2]];bx,bz=b[[0,2]];cx,cz=c[[0,2]]
        denom=(bz-cz)*(ax-cx)+(cx-bx)*(az-cz)
        if abs(denom)<1e-12:continue
        low=np.minimum.reduce([a[[0,2]],b[[0,2]],c[[0,2]]])-1e-7
        high=np.maximum.reduce([a[[0,2]],b[[0,2]],c[[0,2]]])+1e-7
        mask=((points>=low)&(points<=high)).all(axis=1);idx=np.where(mask)[0]
        if not len(idx):continue
        x,z=points[idx].T
        u=((bz-cz)*(x-cx)+(cx-bx)*(z-cz))/denom
        v=((cz-az)*(x-cx)+(ax-cx)*(z-cz))/denom
        w=1-u-v;y=u*a[1]+v*b[1]+w*c[1]
        valid=(u>=-1e-7)&(v>=-1e-7)&(w>=-1e-7)&(y>=lower)&(y<=upper)
        idx=idx[valid];y=y[valid]
        result[idx]=(np.maximum if highest else np.minimum)(result[idx],y)
    return result

def summaries(a):
    return {str(p):float(np.percentile(a,p)) for p in [0,1,5,25,50,75,95,99,100]} if len(a) else None
