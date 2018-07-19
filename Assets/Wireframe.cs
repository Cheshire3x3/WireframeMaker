using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using UnityEngine;

public class Wireframe : MonoBehaviour {

    public float planarTolerance = 0.3f;
    public float wireLineWidth = 0.2f;
    public Material wireMaterial;
    public bool useSerialize = true;

    private string nameExt = "mwf";

    void Awake ()
    {
        /* ADD:
         * - Сжатие файлов
         * - Фоновые генерация, сохранение, чтение (корутины + флаг статуса)
         * - Функции динамической деформации базового mesh синхронно с wireframe
         * - Затупление наружных углов wireframe, если они длиннее заданного порога
        */

        WireframeVT wireframeVT = new WireframeVT();
        string wireframeFileName = Application.streamingAssetsPath + "/" + name + "." + nameExt;

        if ((useSerialize) && (File.Exists(wireframeFileName)))
        {
            Debug.Log("Load wireframe from " + wireframeFileName);

            BinaryFormatter bf = new BinaryFormatter();
            FileStream file = File.Open(wireframeFileName, FileMode.Open);
            wireframeVT = (WireframeVT)bf.Deserialize(file);
        } else
        {
            Debug.Log("Create wireframe for " + name);

            CalcDataWireframeByMesh(GetComponent<MeshFilter>().mesh);
            MakeWireframe(wireframeVT);

            if (useSerialize)
            {
                Debug.Log("Save wireframe to " + wireframeFileName);

                BinaryFormatter bf = new BinaryFormatter();
                FileStream file = File.Create(wireframeFileName);
                bf.Serialize(file, wireframeVT);
                file.Close();
            }
        }

        GameObject Go = new GameObject();
        Go.transform.position = transform.position;
        Go.transform.rotation = transform.rotation;
        Go.transform.localScale = transform.lossyScale;
        Go.transform.parent = transform;
        Go.name = name + "_" + nameExt;

        Go.AddComponent<MeshFilter>().mesh = wireframeVT.CreateMesh(); 
        Go.AddComponent<MeshRenderer>().material = wireMaterial;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////

    private float crossLineParallel = 0.01f;
    private float crossLineTolerance = 0.001f;

    #region INNER DATA

    private class TTris
    {
        public int faceId;
        public Vector3 normalTr;
        public int indA;
        public int indB;
        public int indC;

        public int GetThirdVertice(int inV1, int inV2)
        {
            if (((inV1 == indA) && (inV2 == indB)) || ((inV1 == indB) && (inV2 == indA))) return indC;
            if (((inV1 == indA) && (inV2 == indC)) || ((inV1 == indC) && (inV2 == indA))) return indB;
            if (((inV1 == indB) && (inV2 == indC)) || ((inV1 == indC) && (inV2 == indB))) return indA;
            return -1;
        }
    }

    private class TSide
    {
        public int a;
        public int b;

        public bool Equal(int inA, int inB)
        {
            return (((a == inA) && (b == inB)) || ((a == inB) && (b == inA)));
        }

        public bool Equal(TSide inSide)
        {
            return (((a == inSide.a) && (b == inSide.b)) || ((a == inSide.b) && (b == inSide.a)));
        }

        public bool Equal(int inV)
        {
            return (((a == inV) || (b == inV)));
        }

        public int OtherVert(int inVert)
        {
            if (a == inVert) { return b; }
            else if (b == inVert) { return a; }
            else return -1;
        }
    }

    private class Edge
    {
        public int indTr;
        public TSide side;
        public bool isEmpty;
    }        

    private class Node
    {
        public int indParentTr;
        public TSide indVerts;
        public Vector3 wfVertA;
        public Vector3 wfVertB;
    }

    private class TChain
    {
        public int indParentFace;
        public List<Node> chainNodes;
    }        

    private class TLine
    {
        public Vector3 begin;
        public Vector3 end;
    }

    private TTris[] arrTris;
    private List<Vector3> arrVerts;

    private List<List<Edge>> edges;
    private List<TChain> chains;

    #endregion INNER DATA

    #region Wireframe Serializable

    [System.Serializable]
    public class Vector3Serialize
    {
        public float x, y, z;

        public Vector3Serialize(Vector3 inV)
        {
            x = inV.x;
            y = inV.y;
            z = inV.z;
        }

        public Vector3 GetVector3()
        {
            return new Vector3() { x = this.x, y = this.y, z = this.z }; 
        }
    }

    [System.Serializable]
    public class ListVector3Serialize
    {
        private List<Vector3Serialize> listVector3S = new List<Vector3Serialize>();

        public int IndexOf(Vector3 inV)
        {
            int res = -1;

            for (int i = 0; i < listVector3S.Count; i++)            
                if ((inV.x == listVector3S[i].x) && (inV.y == listVector3S[i].y) && (inV.z == listVector3S[i].z))
                {
                    res = i;
                    break;
                }            

            return res;
        }

        public int Add(Vector3 inV)
        {
            listVector3S.Add(new Vector3Serialize(inV));
            return (listVector3S.Count-1);
        }

        public Vector3[] ToArray()
        {
            Vector3[] res = new Vector3[listVector3S.Count];
            for (int i = 0; i < listVector3S.Count; i++) { res[i] = listVector3S[i].GetVector3(); }
            return res;
        }
    }

    [System.Serializable]
    public class WireframeVT
    {                
        public ListVector3Serialize verts = new ListVector3Serialize();        
        public List<int> tris = new List<int>();

        public int VerticeFindOrAdd(Vector3 inV)
        {            
            int ind = verts.IndexOf(inV);
            if (ind < 0) { ind = verts.Add(inV); }

            return ind;
        }

        public void AddTr(int v1, int v2, int v3)
        {
            tris.Add(v1);
            tris.Add(v2);
            tris.Add(v3);
        }

        public Mesh CreateMesh()
        {
            Mesh m = new Mesh
            {
                vertices = verts.ToArray(),
                triangles = tris.ToArray()
            };
            
            m.RecalculateNormals();
            return m;
        }
    }

    #endregion Wireframe Serializable

    private void CalcDataWireframeByMesh(Mesh srcMesh)
    {
        arrVerts = new List<Vector3>();
        arrTris = new TTris[srcMesh.triangles.Length / 3];

        for (int i = 0; i < arrTris.Length; i++)
        {
            int[] verts = new int[3];

            for (int j = 0; j < 3; j++)
            {
                Vector3 v = srcMesh.vertices[srcMesh.triangles[(i * 3) + j]];
                int iv = arrVerts.IndexOf(v);

                if (iv < 0)
                {
                    arrVerts.Add(v);
                    iv = arrVerts.Count - 1;
                }

                verts[j] = iv;
            }

            Vector3 n = Vector3.Cross(arrVerts[verts[1]] - arrVerts[verts[0]], arrVerts[verts[2]] - arrVerts[verts[0]]);
            n.Normalize();

            arrTris[i] = new TTris { faceId = -1, normalTr = n, indA = verts[0], indB = verts[1], indC = verts[2] };
        }
    }

    private void MakeWireframe(WireframeVT outVT)
    {
        edges = new List<List<Edge>>();
        chains = new List<TChain>();

        // Create Faces
        int indEmptyTr = 0;
        int numFaces = 0;
        do
        {
            edges.Add(new List<Edge>());
            MakeFace(indEmptyTr, numFaces++, null);
            indEmptyTr = FindEmptyTr();
        } while (indEmptyTr != -1);

        // Create Edges Chains
        int numChains = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            int indEmptyEdge = 0;
            do
            {
                chains.Add(new TChain { chainNodes = new List<Node>() });                   
                MakeChain(numChains++, i, indEmptyEdge, edges[i][indEmptyEdge].side.a);
                indEmptyEdge = FindEmptyEdge(i);
            } while (indEmptyEdge != -1);
        }
        
        for (int i = 0; i < chains.Count; i++)
        {
            // Calculate Wireframe Points

            int lastNode = chains[i].chainNodes.Count - 1;

            TLine offsLineFirst = new TLine();
            offsLineFirst = GetOffsetLine(chains[i].chainNodes[0]);

            TLine offsLinePrev = new TLine();
            offsLinePrev = offsLineFirst;

            TLine offsLineLast = new TLine();
            offsLineLast = GetOffsetLine(chains[i].chainNodes[lastNode]);

            Vector3 vCrossFirst = new Vector3();
            vCrossFirst = GetLinesCrossPoint(GetOffsetLine(chains[i].chainNodes[0]), offsLineLast);

            Vector3 vCrossA = new Vector3();
            vCrossA = vCrossFirst;

            TLine offsLineNext = new TLine();
            Vector3 vCrossB = new Vector3();               

            for (int j = 0; j < lastNode; j++)
            {
                if ((j + 1) < lastNode) { offsLineNext = GetOffsetLine(chains[i].chainNodes[j + 1]); } else { offsLineNext = offsLineLast; }
                    
                vCrossB = GetLinesCrossPoint(offsLinePrev, offsLineNext);

                chains[i].chainNodes[j].wfVertA = vCrossA;
                chains[i].chainNodes[j].wfVertB = vCrossB;

                offsLinePrev = offsLineNext;
                vCrossA = vCrossB;
            }

            chains[i].chainNodes[lastNode].wfVertA = vCrossB;
            chains[i].chainNodes[lastNode].wfVertB = vCrossFirst;

            // Make Result Verts and Tris
            for (int j = 0; j < chains[i].chainNodes.Count; j++)
            {
                int indA1 = outVT.VerticeFindOrAdd(arrVerts[chains[i].chainNodes[j].indVerts.a]);
                int indB1 = outVT.VerticeFindOrAdd(arrVerts[chains[i].chainNodes[j].indVerts.b]);
                int indA2 = outVT.VerticeFindOrAdd(chains[i].chainNodes[j].wfVertA);
                int indB2 = outVT.VerticeFindOrAdd(chains[i].chainNodes[j].wfVertB);

                outVT.AddTr(indA2, indA1, indB1);
                outVT.AddTr(indB1, indB2, indA2);
            }
        }
    }

    private Vector3 GetLinesCrossPoint(TLine linePrev, TLine lineActual)
    {
        Vector3 res = new Vector3();

        bool fCrossInLines = false;
        if (!(PointCrossLines(linePrev.begin, linePrev.end, lineActual.begin, lineActual.end, ref res, ref fCrossInLines)))
        {
            res = linePrev.begin;   // ???
        }
        // + Расчёт и вставка доп. треугольников в острые внешние углы (!fCrossInLines)

        return res;
    }

    private TLine GetOffsetLine(Node inNode)
    {            
        Vector3 parentTrNormal = arrTris[inNode.indParentTr].normalTr;

        Vector3 a = arrVerts[inNode.indVerts.a];
        Vector3 b = arrVerts[inNode.indVerts.b];
        Vector3 c = arrVerts[arrTris[inNode.indParentTr].GetThirdVertice(inNode.indVerts.a, inNode.indVerts.b)];

        Vector3 n = Vector3.Cross(b, a);
        if (Vector3.Dot(n,c) < 0) n = Vector3.Cross(a, b);

        Vector3 tangent = b - a;
        Vector3.OrthoNormalize(ref parentTrNormal, ref tangent, ref n);
        n *= wireLineWidth;

        return new TLine() { begin = b + n, end = a + n };
    }

    #region EDGES CHAINS

    private int FindEmptyEdge(int indFace)
    {
        int res = -1;

        for (int i = 0; i < edges[indFace].Count; i++)
            if (edges[indFace][i].isEmpty) { res = i; break; }

        return res;
    }

    private int FindEmptyEdgeByNode(int indFace, int node)
    {
        int res = -1;

        for (int i = 0; i < edges[indFace].Count; i++)
            if ((edges[indFace][i].isEmpty) && (edges[indFace][i].side.Equal(node))) { res = i; break; }

        return res;
    }

    private void MakeChain(int indChain, int indFace, int indEdge, int vertA)
    {
        edges[indFace][indEdge].isEmpty = false;
        int vertB = edges[indFace][indEdge].side.OtherVert(vertA);

        chains[indChain].indParentFace = indFace;
        chains[indChain].chainNodes.Add(new Node { indParentTr = edges[indFace][indEdge].indTr, indVerts = new TSide { a = vertA, b = vertB } });

        int i = FindEmptyEdgeByNode(indFace, vertB);
        if (i > -1) MakeChain(indChain, indFace, i, vertB);
    }

    #endregion EDGES CHAINS

    #region FACES

    private void MakeFace(int indBaseTr, int inFaceId, TSide fromSide)
    {
        arrTris[indBaseTr].faceId = inFaceId;

        TSide[] trSides = SidesToFind(indBaseTr,fromSide);

        for (int i = 0; i < trSides.Length; i++)
        {
            int indFindTr = FindEmptyTrByEdge(indBaseTr, trSides[i]);

            if ((indFindTr > -1) && ((arrTris[indBaseTr].normalTr - arrTris[indFindTr].normalTr).sqrMagnitude < planarTolerance))
            {
                MakeFace(indFindTr, inFaceId, trSides[i]);
            }
            else
            {
                int dblEdge = FindEdgeBySide(edges[inFaceId], trSides[i]);
                if (dblEdge < 0)
                {
                    edges[inFaceId].Add(new Edge { indTr = indBaseTr, side = trSides[i], isEmpty = true });
                }
                else edges[inFaceId].RemoveAt(dblEdge);
            }

        }
    }

    private TSide[] SidesToFind(int indTr, TSide fromSide)
    {
        TSide[] res;

        if (fromSide == null)
        {
            res = new TSide[3] { new TSide { a = arrTris[indTr].indA, b = arrTris[indTr].indB },
                                    new TSide { a = arrTris[indTr].indB, b = arrTris[indTr].indC },
                                    new TSide { a = arrTris[indTr].indC, b = arrTris[indTr].indA } };
        }
        else
        {
            res = new TSide[2];

            if (fromSide.Equal(arrTris[indTr].indA, arrTris[indTr].indB))
            {
                res = new TSide[2] { new TSide { a = arrTris[indTr].indB, b = arrTris[indTr].indC },
                                        new TSide { a = arrTris[indTr].indC, b = arrTris[indTr].indA } };
            }

            if (fromSide.Equal(arrTris[indTr].indB, arrTris[indTr].indC))
            {
                res = new TSide[2] { new TSide { a = arrTris[indTr].indA, b = arrTris[indTr].indB },
                                        new TSide { a = arrTris[indTr].indC, b = arrTris[indTr].indA } };
            }

            if (fromSide.Equal(arrTris[indTr].indC, arrTris[indTr].indA))
            {
                res = new TSide[2] { new TSide { a = arrTris[indTr].indA, b = arrTris[indTr].indB },
                                        new TSide { a = arrTris[indTr].indB, b = arrTris[indTr].indC } };
            }
        }

        return res;
    }

    private int FindEdgeBySide(List<Edge> listEdges, TSide side)
    {
        int res = -1;

        for (int i = 0; i < listEdges.Count; i++)
            if (side.Equal(listEdges[i].side)) { res = i; break; }

        return res;
    }

    private int FindEmptyTr()
    {
        int res = -1;

        for (int i = 0; i < arrTris.Length; i++)
            if (arrTris[i].faceId == -1) { res = i; break; }

        return res;
    }

    private int FindEmptyTrByEdge(int indBaseTr, TSide side)
    {
        int res = -1;

        for (int i = 0; i < arrTris.Length; i++)
            if ((i != indBaseTr) && (arrTris[i].faceId == -1))
                {   
                    if (side.Equal(arrTris[i].indA, arrTris[i].indB) ||
                        side.Equal(arrTris[i].indB, arrTris[i].indC) ||
                        side.Equal(arrTris[i].indC, arrTris[i].indA))
                        {
                            res = i;
                            break;
                        }
            }
            
        return res;
    }

    #endregion FACES

    #region ROUTINES

    private bool PointCrossLines(Vector3 A1, Vector3 A2, Vector3 B1, Vector3 B2, ref Vector3 cross, ref bool fCrossIn)
    {
        Vector3 dirA = A2 - A1;
        Vector3 dirB = B2 - B1;
        Vector3 diff = A1 - B1;

        float ab = Vector3.Dot(dirA, dirB);
        float ad = Vector3.Dot(dirA, diff);
        float bd = Vector3.Dot(dirB, diff);
        float sqrDirA = Vector3.Dot(dirA, dirA);   
        float sqrDirB = Vector3.Dot(dirB, dirB);   

        float denom = sqrDirA * sqrDirB - ab * ab;

        if (Mathf.Abs(denom) < crossLineParallel) return false;

        float num = (bd * ab) - (ad * sqrDirB);

        float kA = num / denom;
        float kB = (bd + (ab * kA)) / sqrDirB;

        Vector3 crossA = A1 + (kA * dirA);
        Vector3 crossB = B1 + (kB * dirB);

        // Для вставки доп. треугольников в острые внешние углы:
        //fCrossIn = ((0 <= kA) && ((kA * kA) <= dirA.sqrMagnitude)) && ((0 <= kB) && ((kB * kB) <= dirB.sqrMagnitude));

        if ((crossB - crossA).sqrMagnitude < crossLineTolerance)
        {
            cross = crossA;
            return true;
        } return false;
    }

    #endregion ROUTINES

}
