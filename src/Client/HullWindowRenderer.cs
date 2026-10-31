using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Hansa
{
    public partial class ShipRenderer
    {
        void AddHullWindows(EntityShip ship,Decor decor)
        {
            if(ship.HullPanels==null) return;
            for(int n=0;n<ship.HullPanels.Count;n++)
            {
                if(ship.HullPanels[n]!=SlotState.Glazed) continue;
                var parts=HullWindows.Parts(ship.HullLayout.Slots[n]);
                foreach(bool glass in new[]{false,true})
                {
                    var data=new MeshData(128,192,false,true,true,true);
                    foreach(var q in ModularMesh.Quads(parts,keepShared:false))
                    {
                        var mat=(PartMat)(int)q[0];if((mat==PartMat.Glass)!=glass) continue;
                        var tex=AtlasTex(mat==PartMat.Glass?"hansa:block/glass-clear":mat==PartMat.Lead?"game:block/metal/plate/lead":"game:block/wood/debarked/oak",null);
                        int first=data.VerticesCount,flags=VertexFlags.PackNormal(q[6],q[7],q[8]);
                        for(int k=0;k<4;k++) data.AddWithFlagsVertex(q[1+8*k],q[2+8*k],q[3+8*k],tex.x1+(tex.x2-tex.x1)*q[4+8*k]/16,tex.y1+(tex.y2-tex.y1)*q[5+8*k]/16,-1,flags);
                        data.AddIndices(first,first+1,first+2,first,first+2,first+3);data.AddTextureId(tex.atlasTextureId);
                    }
                    if(data.VerticesCount==0) continue;
                    var piece=new Piece {Fixture=new FixtureDef {Code="hull-window-"+n,Type="piece",Pos=ship.HullLayout.Slots[n].Centre.ToDoubleArray()},Glass=glass};
                    Mat4f.Identity(piece.Fit);piece.Mesh=capi.Render.UploadMultiTextureMesh(data);decor.Pieces.Add(piece);
                }
            }
        }
    }
}
