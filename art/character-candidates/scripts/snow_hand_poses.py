"""Diagnostic, authored gesture poses for the single Snow hand-quality candidate."""
import math
from mathutils import Vector,Quaternion,Matrix
DIGITS=['Thumb','Index','Middle','Ring','Pinky']
def reset(rig):
 rig.location=(0,0,0)
 for b in rig.pose.bones:b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion();b.location=(0,0,0);b.scale=(1,1,1)
def rot(rig,n,axis,deg,append=False):
 q=rig.data.bones[n].matrix_local.to_quaternion();local=q.inverted()@Quaternion(Vector(axis),math.radians(deg))@q
 b=rig.pose.bones[n];b.rotation_quaternion=b.rotation_quaternion@local if append else local

def relaxed(rig):
 reset(rig)
 rot(rig,'UpperArm.L',(0,1,0),76);rot(rig,'UpperArm.R',(0,1,0),-76)
 rot(rig,'Forearm.L',(0,0,1),-10);rot(rig,'Forearm.R',(0,0,1),10)

def phone_grip(rig,full_body=True):
 if full_body:
  relaxed(rig);rot(rig,'UpperArm.L',(0,1,0),68);rot(rig,'Forearm.L',(0,0,1),-87);rot(rig,'Hand.L',(1,0,0),10);rot(rig,'Head',(0,0,1),22);rot(rig,'Head',(1,0,0),16,True)
 else:reset(rig)
 for digit,angles in {'Index':(-8,88,86),'Middle':(-8,90,88),'Ring':(-8,90,86),'Pinky':(2,85,86)}.items():
  for j,a in enumerate(angles,1):rot(rig,f'{digit}{j}.L',(0,1,0),a)
 rot(rig,'Thumb1.L',(0,1,0),-15);rot(rig,'Thumb1.L',(0,0,1),10,True)
 rot(rig,'Thumb2.L',(0,1,0),-5);rot(rig,'Thumb3.L',(0,1,0),15)

def wave(rig,full_body=True):
 if full_body:
  relaxed(rig);rot(rig,'UpperArm.L',(0,1,0),-24);rot(rig,'Forearm.L',(0,1,0),-65);rot(rig,'Hand.L',(1,0,0),78)
 else:reset(rig)
 for digit,spread in [('Index',-12),('Middle',-3),('Ring',5),('Pinky',15)]:
  rot(rig,digit+'1.L',(0,0,1),spread)
  for j,angle in [(1,-7),(2,-4),(3,-3)]:rot(rig,f'{digit}{j}.L',(0,1,0),angle,True)
 rot(rig,'Thumb1.L',(0,0,1),-12);rot(rig,'Thumb1.L',(0,1,0),-10,True)

def world_rotations(rig):
 out={}
 for b in rig.pose.bones:
  q=rig.data.bones[b.name].matrix_local.to_quaternion();r=q@b.rotation_quaternion@q.inverted();out[b.name]=list(r)
 return out

def apply_world_rotations(rig,rotations):
 reset(rig)
 for name,values in rotations.items():
  q=rig.data.bones[name].matrix_local.to_quaternion();rig.pose.bones[name].rotation_quaternion=q.inverted()@Quaternion(values)@q

# Phone dimensions: 70 x 150 x 10 mm. World axes here describe the source T-pose hand frame.
PHONE_CENTER=Vector((.813,.007,1.395));PHONE_SIZE=Vector((.070,.150,.010))
def phone_matrix(rig):return rig.matrix_world@rig.pose.bones['Hand.L'].matrix@rig.data.bones['Hand.L'].matrix_local.inverted()
