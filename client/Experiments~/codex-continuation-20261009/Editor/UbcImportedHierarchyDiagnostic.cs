#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CTLife.EditorTools
{
    // Read existing imported assets only. No Instantiate, reimport, scene selection or persistence APIs.
    public static class UbcImportedHierarchyDiagnostic
    {
        const string Root = "Assets/CTLifeCandidates/UBCImportProbe20261009B";
        const string EvidenceRoot = "/Users/mac/Documents/codes/projects/ct-life-evidence/claude-continuation-20261009/";
        const string PlanPath = EvidenceRoot + "ubc-import-probe/source-plan.json";
        const string PlanSha = "7c05bb7b31cce2727013925985f0b171de56482d59b84d079b0978d83e51b987";
        static string Sha(string path) { using (var s=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
        static void Need(bool value,string message) { if(!value) throw new InvalidDataException(message); }
        static string Arg(string key) { var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);if(i<0||i+1>=a.Length||a[i+1].StartsWith("-",StringComparison.Ordinal))throw new ArgumentException(key);return a[i+1]; }
        public static void Run()
        {
            int code=1;string outPath=null;bool ownsOutput=false;var scene=SceneManager.GetActiveScene();int sceneCount=SceneManager.sceneCount;
            var hashes=new Dictionary<string,string>(StringComparer.Ordinal);string[] candidatePaths=Array.Empty<string>();
            var report=new JObject { ["scope"]="Read-only existing candidate B imported hierarchy; no native animation/art acceptance",["status"]="started",["candidateRoot"]=Root,["pass"]=false };
            try
            {
                Need(Application.isBatchMode&&!EditorApplication.isPlayingOrWillChangePlaymode,"Diagnostic requires its root-owned Edit-mode batch process.");
                Need(Application.unityVersion=="6000.3.25f1","Unexpected Unity version.");Need(Directory.Exists(Root),"Existing candidate B is missing.");
                outPath=Path.GetFullPath(Arg("-ctUbcDiagnosticOut"));Need(outPath.StartsWith(EvidenceRoot,StringComparison.Ordinal)&&!Directory.Exists(outPath)&&!File.Exists(outPath),"Use a fresh external evidence directory.");
                Directory.CreateDirectory(outPath);ownsOutput=true;Need(Sha(PlanPath)==PlanSha,"Frozen original plan changed.");
                var plan=JObject.Parse(File.ReadAllText(PlanPath));var files=(JObject)plan["files"];string source=(string)plan["sourceRoot"];
                candidatePaths=Directory.GetFiles(Root,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
                foreach(var p in candidatePaths)hashes[p]=Sha(p);
                foreach(var p in files.Properties()) { var original=Path.Combine(source,p.Name);Need(Sha(original)==(string)p.Value,"Original input changed: "+p.Name);hashes[original]=Sha(original);Need(Sha(Root+"/source/"+p.Name)==(string)p.Value,"Copied input bytes differ: "+p.Name); }
                hashes[PlanPath]=PlanSha;report["protectedBefore"]=JObject.FromObject(hashes);
                var paths=files.Properties().Where(p=>p.Name.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase)).Select(p=>Root+"/source/"+p.Name).Concat(new[]{Root+"/CanonicalRig.prefab"}).ToArray();
                Need(paths.Length==10,"Expected nine FBX assets and the canonical rig.");var assets=new JArray();report["assets"]=assets;
                foreach(var path in paths)assets.Add(Read(path));
                report["allRequestedAssetsLoaded"]=assets.Cast<JObject>().All(a=>(bool)a["loaded"]);
                Need((bool)report["allRequestedAssetsLoaded"],"One or more requested existing assets did not load.");
                report["status"]="captured_readonly_hierarchy_not_art_acceptance";code=0;
            }
            catch(Exception e) { report["failure"]=e.GetType().Name+": "+e.Message;Debug.LogException(e); }
            finally
            {
                var drift=new JArray();foreach(var p in hashes)if(!File.Exists(p.Key)||Sha(p.Key)!=p.Value)drift.Add(p.Key);
                var added=new JArray(Directory.Exists(Root)?Directory.GetFiles(Root,"*",SearchOption.AllDirectories).Except(candidatePaths,StringComparer.Ordinal):Array.Empty<string>());
                bool sceneSame=SceneManager.GetActiveScene()==scene&&SceneManager.sceneCount==sceneCount;
                report["protectedDrift"]=drift;report["unexpectedCandidateFiles"]=added;report["activeSceneUnchanged"]=sceneSame;
                report["instantiatedObjects"]=0;report["reimportCalled"]=false;report["saveCalled"]=false;
                if(drift.Count!=0||added.Count!=0||!sceneSame)code=1;report["pass"]=code==0;
                try { if(ownsOutput)File.WriteAllText(Path.Combine(outPath,"imported-hierarchy.json"),report.ToString()); }
                catch(Exception e) { code=1;Debug.LogError("[UBC hierarchy] Receipt failed: "+e.Message); }
                finally { Debug.Log("[UBC hierarchy] "+(code==0?"CAPTURED":"FAILED")+"; output="+outPath);if(Application.isBatchMode)EditorApplication.Exit(code); }
            }
        }
        static JObject Read(string path)
        {
            var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);var row=new JObject { ["path"]=path,["sha256"]=Sha(path),["loaded"]=go!=null };
            if(go==null)return row;
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer!=null)row["importer"]=new JObject { ["animationType"]=importer.animationType.ToString(),["avatarSetup"]=importer.avatarSetup.ToString(),["sourceAvatar"]=importer.sourceAvatar?AssetDatabase.GetAssetPath(importer.sourceAvatar):null,["importAnimation"]=importer.importAnimation,["isReadable"]=importer.isReadable,["optimizeGameObjects"]=importer.optimizeGameObjects,["bakeAxisConversion"]=importer.bakeAxisConversion,["useFileScale"]=importer.useFileScale,["globalScale"]=importer.globalScale,["skinWeights"]=importer.skinWeights.ToString() };
            var renderers=new JArray();row["renderers"]=renderers;
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var skin=r as SkinnedMeshRenderer;var filter=r.GetComponent<MeshFilter>();var mesh=skin!=null?skin.sharedMesh:filter!=null?filter.sharedMesh:null;
                var rec=new JObject { ["concreteType"]=r.GetType().FullName,["gameObjectName"]=r.gameObject.name,["componentName"]=r.name,["path"]=PathOf(r.transform,go.transform),["enabled"]=r.enabled,["activeSelf"]=r.gameObject.activeSelf,["localToWorld"]=Matrix(r.localToWorldMatrix),["materials"]=new JArray(r.sharedMaterials.Select(m=>m!=null?m.name:null)),["mesh"]=MeshInfo(mesh) };
                if(skin!=null) { rec["boneCount"]=skin.bones.Length;rec["bonePaths"]=new JArray(skin.bones.Select(b=>b!=null?PathOf(b,go.transform):null));rec["boneNames"]=new JArray(skin.bones.Select(b=>b!=null?b.name:null));rec["rootBone"]=skin.rootBone?PathOf(skin.rootBone,go.transform):null; }
                renderers.Add(rec);
            }
            row["animators"]=new JArray(go.GetComponentsInChildren<Animator>(true).Select(a=>new JObject { ["path"]=PathOf(a.transform,go.transform),["enabled"]=a.enabled,["avatar"]=AvatarInfo(a.avatar),["controller"]=a.runtimeAnimatorController?AssetDatabase.GetAssetPath(a.runtimeAnimatorController):null }));
            row["assetAvatars"]=new JArray(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Select(AvatarInfo));
            row["assetMeshes"]=new JArray(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Select(MeshInfo));
            row["lodGroups"]=new JArray(go.GetComponentsInChildren<LODGroup>(true).Select(l=>new JObject { ["path"]=PathOf(l.transform,go.transform),["lods"]=new JArray(l.GetLODs().Select(x=>new JObject { ["screenRelativeTransitionHeight"]=x.screenRelativeTransitionHeight,["rendererPaths"]=new JArray(x.renderers.Select(r=>r?PathOf(r.transform,go.transform):null)) })) }));
            row["hierarchy"]=new JArray(go.GetComponentsInChildren<Transform>(true).Select(t=>new JObject { ["path"]=PathOf(t,go.transform),["name"]=t.name,["parent"]=t.parent?PathOf(t.parent,go.transform):null,["localPosition"]=Vec(t.localPosition),["localRotation"]=new JArray(t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w),["localScale"]=Vec(t.localScale),["components"]=new JArray(t.GetComponents<Component>().Select(c=>c?c.GetType().FullName:"missing")) }));
            return row;
        }
        static JObject MeshInfo(Mesh m)
        {
            if(m==null)return null;long tris=0;var sub=new JArray();for(int i=0;i<m.subMeshCount;i++){uint n=m.GetIndexCount(i);var topology=m.GetTopology(i);if(topology==MeshTopology.Triangles)tris+=n/3;sub.Add(new JObject { ["topology"]=topology.ToString(),["indices"]=n });}
            var j=new JObject { ["name"]=m.name,["assetPath"]=AssetDatabase.GetAssetPath(m),["vertices"]=m.vertexCount,["triangles"]=tris,["isReadable"]=m.isReadable,["submeshes"]=sub,["bindposeCount"]=m.bindposes.Length };
            if(m.isReadable) { var counts=m.GetBonesPerVertex();int max=0;foreach(var c in counts)max=Math.Max(max,c);j["bonesPerVertexRecords"]=counts.Length;j["maxInfluences"]=max; }
            return j;
        }
        static JObject AvatarInfo(Avatar a)=>a==null?null:new JObject { ["name"]=a.name,["path"]=AssetDatabase.GetAssetPath(a),["human"]=a.isHuman,["valid"]=a.isValid };
        static string PathOf(Transform t,Transform root)=>AnimationUtility.CalculateTransformPath(t,root);
        static JArray Vec(Vector3 v)=>new JArray(v.x,v.y,v.z);
        static JArray Matrix(Matrix4x4 m)=>new JArray(Enumerable.Range(0,16).Select(i=>m[i]));
    }
}
#endif
