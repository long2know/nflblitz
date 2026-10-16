using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;

internal static class PoseChecks
{
    private static int checks;
    private static SourcePlayer player;
    internal static int Run(string model, string skeleton)
    {
        checks=0;
        player=GeneratedPlayerReader.Read(model,"models/own.h",skeleton,"models/own-rig.h",
            new PlayerSymbolSelection("model_parts","test_skeleton","rig_parts","material_slots"));
        SourcePlayerPose pose=Read(Make());
        Equal(pose.Sequence,"SYNTHETIC","pose sequence exact");
        Equal(pose.SourceFrame,0,"pose source frame exact");
        Equal(pose.ExportedFrame,0,"pose exported frame exact");
        var conversion=new PlayerCoordinateConversion(SignedSourceAxis.PositiveX,SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ,SourceTriangleOrder.Renderer321);
        AssetVector3 root=pose.UnityAnchoredRootOffset(conversion);
        Near(root.X,0,"pose source XZ anchor X");Near(root.Y,4,"pose source root Y retained");Near(root.Z,0,"pose source XZ anchor Z");
        var rotations=pose.UnityLocalRotations(conversion);
        Near(rotations[0].X,-(float)Math.Sqrt(0.5),"pose reflection axial X sign");
        Near(rotations[1].Z,(float)Math.Sqrt(0.5),"pose reflection axial Z sign");
        var reference=new SourcePoseEvaluator(player,pose,conversion);
        AssetVector3 fixedExpected=reference.SourceVertexWorld(1,new AssetVector3(3,2,0));
        Near(fixedExpected.X,3,"pose known transformed point X");Near(fixedExpected.Y,6,"pose known transformed point Y");
        Near(fixedExpected.Z,-2,"pose known transformed point Z");
        int maps=0;
        foreach(SignedSourceAxis x in Enum.GetValues(typeof(SignedSourceAxis)))
        foreach(SignedSourceAxis y in Enum.GetValues(typeof(SignedSourceAxis)))
        foreach(SignedSourceAxis z in Enum.GetValues(typeof(SignedSourceAxis)))
        {
            if((int)x/2==(int)y/2||(int)x/2==(int)z/2||(int)y/2==(int)z/2)continue;
            var c=new PlayerCoordinateConversion(x,y,z,SourceTriangleOrder.Declared123);
            var evaluator=new SourcePoseEvaluator(player,pose,c);
            var q=pose.UnityLocalRotations(c);
            var jointMatrices=new double[player.Nodes.Count][];
            for(int i=0;i<player.Nodes.Count;i++)
            {
                int parent=player.Nodes[i].ParentIndex;
                AssetVector3 pivot=c.Convert(player.Nodes[i].BindPivot);
                AssetVector3 pp=parent<0?new AssetVector3():c.Convert(player.Nodes[parent].BindPivot);
                double[] pm=parent<0?T(pose.UnityAnchoredRootOffset(c)):jointMatrices[parent];
                jointMatrices[i]=Mul(Mul(pm,T(pivot-pp)),Q(q[i]));
                foreach(AssetVector3 vertex in player.Limbs[i].Vertices)
                {
                    AssetVector3 local=c.Convert(vertex)-pivot;
                    AssetVector3 actual=Point(jointMatrices[i],local);
                    AssetVector3 expected=evaluator.SourceVertexWorld(i,vertex);
                    Near(actual.X,expected.X,"pose standard parent-local equals source active pivot X");
                    Near(actual.Y,expected.Y,"pose standard parent-local equals source active pivot Y");
                    Near(actual.Z,expected.Z,"pose standard parent-local equals source active pivot Z");
                    AssetVector3 mesh=evaluator.MeshVertexWorld(i,local);
                    Near(mesh.X,expected.X,"pose source mesh inverse-basis X");Near(mesh.Y,expected.Y,"pose source mesh inverse-basis Y");
                    Near(mesh.Z,expected.Z,"pose source mesh inverse-basis Z");
                }
            }
            maps++;
        }
        Equal(maps,48,"pose all signed axis maps checked");
        Dictionary<string,object> doc=Make();byte[] bytes=Bytes(doc);
        Bad(()=>SourcePlayerPoseReader.Read(bytes,"pose.json",new string('0',64),player),"content pin","pose explicit hash gate");
        BadDoc(d=>d["schema"]="wrong","expected complete","pose schema");
        BadDoc(d=>((Dictionary<string,object>)d["sequence"])["exported_frame_index"]=1,"first-frame indices","pose not arbitrary clip frame");
        BadDoc(d=>((Dictionary<string,object>)d["sequence"])["source_frame_index"]=2,"first-frame indices","pose source frame bounds");
        BadDoc(d=>((Dictionary<string,object>)d["sequence"])["source_frame_count"]=1025,"first-frame indices","pose frame count bound");
        BadDoc(d=>((Dictionary<string,object>)d["provenance"])["content_pin_verified"]=false,"verified source-content","pose upstream content pin");
        BadDoc(d=>((Dictionary<string,object>)d["root_translation"])["runtime_scale"]=0.25,"Scaled root translation","pose scale mismatch");
        BadDoc(d=>Joint(d,0)["quaternion_xyzw"]=new double[]{0,0,0,0},"unit length","pose zero quaternion");
        BadDoc(d=>Joint(d,0)["rotation_matrix_row_major"]=new double[]{1,0,0,0,1,0,0,0,1},"matrix/quaternion mismatch","pose matrix/quat mismatch");
        BadDoc(d=>Joint(d,1)["parent_index"]=1,"mapping mismatch","pose parent mapping");
        BadDoc(d=>Joint(d,0)["parent_index"]=0,"mapping mismatch","pose root must be null parent");
        BadDoc(d=>Joint(d,1)["source_limb_id"]=1,"mapping mismatch","pose source limb IDs");
        BadDoc(d=>Joint(d,1)["fbplyr_skel_index"]=0,"mapping mismatch","pose duplicate skeleton index");
        BadDoc(d=>Joint(d,1)["name"]="root","Duplicate pose joint","pose duplicate joint name");
        BadDoc(d=>Joint(d,0).Remove("quaternion_xyzw"),"invalid bounded pose JSON","pose missing required field");
        byte[] malformed=Encoding.UTF8.GetBytes("{");
        Bad(()=>SourcePlayerPoseReader.Read(malformed,"pose.json",AuthorizedAssetRoot.Sha256(malformed),player),"2..262144","pose byte lower bound");
        return checks;
    }
    private static Dictionary<string,object> Make()
    {
        double s=Math.Sqrt(0.5);
        return new Dictionary<string,object>{
            {"schema","original-wan-first-pose/v1"},
            {"sequence",new Dictionary<string,object>{{"runtime_name","SYNTHETIC"},{"limb_count",2},{"source_frame_index",0},
                {"exported_frame_index",0},{"source_frame_count",2},{"selected_frame_count",2}}},
            {"root_translation",new Dictionary<string,object>{{"authoring_xyz",new double[]{2,8,6}},
                {"runtime_scale",0.5},{"scaled_runtime_xyz",new double[]{1,4,3}}}},
            {"provenance",new Dictionary<string,object>{{"path","synthetic/own.wan"},{"git_blob_sha1",new string('a',40)},
                {"sha256",new string('b',64)},{"content_pin_verified",true}}},
            {"joints",new object[]{J("root",0,null,new double[]{s,0,0,s}),J("child",1,0,new double[]{0,0,s,s})}}
        };
    }
    private static object J(string name,int index,int? parent,double[] q)
    {
        double[] m=Q(new AssetQuaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]));
        return new Dictionary<string,object>{{"name",name},{"fbplyr_skel_index",index},{"source_limb_id",index+1},
            {"parent_index",parent},{"quaternion_xyzw",q},
            {"rotation_matrix_row_major",new[]{m[0],m[1],m[2],m[4],m[5],m[6],m[8],m[9],m[10]}},
            {"authoring_offset_ignored_by_converter",new double[]{97,83,29}}};
    }
    private static Dictionary<string,object> Joint(Dictionary<string,object>d,int i)
    {
        return (Dictionary<string,object>)((object[])d["joints"])[i];
    }
    private static byte[] Bytes(Dictionary<string,object>d){return JsonSerializer.SerializeToUtf8Bytes(d);}
    private static SourcePlayerPose Read(Dictionary<string,object>d)
    {
        byte[] b=Bytes(d);return SourcePlayerPoseReader.Read(b,"pose.json",AuthorizedAssetRoot.Sha256(b),player);
    }
    private static void BadDoc(Action<Dictionary<string,object>> change,string fragment,string label)
    {
        var d=Make();change(d);Bad(()=>Read(d),fragment,label);
    }
    private static void Bad(Action a,string fragment,string label)
    {
        try{a();}catch(PlayerAssetException e){Check(e.Message.Contains(fragment),label+": "+e.Message);return;}
        throw new Exception("ASSERT "+label+": expected PlayerAssetException");
    }
    private static void Equal<T>(T a,T b,string label){Check(EqualityComparer<T>.Default.Equals(a,b),label);}
    private static void Near(float a,float b,string label){Check(!float.IsNaN(a)&&Math.Abs(a-b)<0.00001f,label+": "+a+" vs "+b);}
    private static void Check(bool ok,string label){checks++;if(!ok)throw new Exception("ASSERT "+label);}
    private static double[] T(AssetVector3 p){return new double[]{1,0,0,p.X,0,1,0,p.Y,0,0,1,p.Z,0,0,0,1};}
    private static double[] Q(AssetQuaternion q)
    {
        double x=q.X,y=q.Y,z=q.Z,w=q.W;
        return new[]{1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),0,2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),0,
            2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),0,0,0,0,1d};
    }
    private static double[] Mul(double[]a,double[]b)
    {
        var m=new double[16];for(int r=0;r<4;r++)for(int c=0;c<4;c++)for(int k=0;k<4;k++)m[r*4+c]+=a[r*4+k]*b[k*4+c];return m;
    }
    private static AssetVector3 Point(double[]m,AssetVector3 p)
    {
        return new AssetVector3((float)(m[0]*p.X+m[1]*p.Y+m[2]*p.Z+m[3]),(float)(m[4]*p.X+m[5]*p.Y+m[6]*p.Z+m[7]),
            (float)(m[8]*p.X+m[9]*p.Y+m[10]*p.Z+m[11]));
    }
}
