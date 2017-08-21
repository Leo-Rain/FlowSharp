﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SlimDX;
using static FlowSharp.Octree;

namespace FlowSharp
{
    class TetTreeGrid : FieldGrid, GeneralUnstructurdGrid
    {
        //public VectorList _vertices;
        public VectorData Vertices { get; set; }// { get { return _vertices; } set { _vertices = value as VectorList; } }

        public IndexArray Cells;
        private VectorBuffer _cellCenters;
        public int NumCells { get { return Cells.Length; } }

        public Octree Tree;
        public float CellSizeReference { get; protected set; }

        /// <summary>
        /// Assemble all inidices to a buffer. Do this here for general Tet grids.
        /// </summary>
        /// <returns></returns>
        public Tuple<VectorData, IndexArray> AssembleIndexList()
        {
            //Index[] cells = new Index[Cells.Length];
            //for (int c = 0; c < Cells.Length; ++c)
            //        cells[c] = Cells[c].VertexIndices;
            //return cells;
            IndexArray tris = new IndexArray(Cells.Length, 4);
            for (int c = 0; c < Cells.Length; c++)
            {
                for (int s = 0; s < 4; ++s)
                {
                    tris[c * 4 + s] = new Index(3);
                    int count = 0;
                    for (int i = 0; i < 4; ++i)
                        if (i != s)
                            tris[c * 4 + s][count++] = Cells[c][i];
                }
            }

            return new Tuple<VectorData, IndexArray>(Vertices, tris);
        }

        public IndexArray BorderGeometry()
        {
            // ========== Setup Vertex to Side List ========== \\
            // Assemble all sides at assigned vertices. Delete them later.
            List<Index>[] sidesPerVertex = new List<Index>[Vertices.Length];
            for (int v = 0; v < Vertices.Length; ++v)
                sidesPerVertex[v] = new List<Index>();

            for (int c = 0; c < Cells.Length; ++c)
            {

                if (c % (Cells.Length / 100) == 0)
                    Console.WriteLine("Buildup done for {0}% cells.", c / (Cells.Length / 100));
                // Save at all connected vertices.
                for (int s = 0; s < 4; ++s)
                {
                    // Remove s'ths index to get a side.
                    Index side = new Index(Cells[c]);
                    side[s] = side[3];
                    side = side.ToIntX(3);

                    // Sort indices to make comparable.
                    Array.Sort(side.Data);

                    // Add to all connected vertex side lists.
                    //for (int v = 0; v < 3; ++v)
                        sidesPerVertex[side[0]].Add(side);
                }
            }

            List<Index> remainingSides = new List<Index>();

            // ========== Remove All Sides that Appear Multiple Times ========== \\
            for (int v = 0; v < Vertices.Length; ++v)
            {
                var sides = sidesPerVertex[v].ToArray();
                Array.Sort(sides, Index.Compare);

                if (v % (Vertices.Length / 100) == 0)
                    Console.WriteLine("Finished sorting through {0}% triangles.\n\t{1} border triangles found.", v / (Vertices.Length / 100), remainingSides.Count);
                // sidesPerVertex[v] = sides.ToList();

                for (int s = sides.Length - 1; s >= 0; s--)
                {
                    if (s > 0 && sides[s] == sides[s-1])
                    {
                        //sidesPerVertex[v].RemoveRange(s - 1, 2);
                        s--;
                    }
                    else
                    {
                        remainingSides.Add(sides[s]);

                        //bool exists = sidesPerVertex[sides[s][1]].Remove(sides[s]);
                        //Debug.Assert(exists);
                        //exists = sidesPerVertex[sides[s][2]].Remove(sides[s]);
                        //Debug.Assert(exists);
                    }
                    

                    //// Not the first side that should have seen this side. Should be deleted then.
                    //if (side[0] != v)
                    //    continue;

                    //if (sidesPerVertex[side[1]].Remove(side))
                    //{
                    //    bool test = sidesPerVertex[side[2]].Remove(side);
                    //    Debug.Assert(test, "Side should be contained ")
                    //}
                }
            }

            return new IndexArray(remainingSides.ToArray(), 3);
        }

        public TetTreeGrid(UnstructuredGeometry geom, int maxNumVertices = 100, int maxLevel = 10, Vector origin = null, float? timeOrigin = null) : this(geom.Vertices, geom.Primitives, maxNumVertices, maxLevel, origin, timeOrigin) { }

        /// <summary>
        /// Create a new tetraeder grid descriptor.
        /// </summary>
        public TetTreeGrid(VectorData vertices, IndexArray indices, int maxNumVertices = 10, int maxDepth = 10, Vector origin = null, float? timeOrigin = null)
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();

            // For Dimensionality.
            Size = new Index(4);
            Cells = indices;
            //            Cells = new Tet[indices.Length * 5];
            Vertices = vertices;
            Vertices.ExtractMinMax();

            Debug.Assert(vertices.Length > 0 && indices.Length > 0, "No data given.");
            Debug.Assert(indices.IndexLength == 4, "Not tets.");
            int dim = vertices[0].Length;

            // Space position.
            Origin = origin ?? new Vector(0, 4);
            TimeDependant = timeOrigin != null;
            Origin.T = timeOrigin ?? Origin.T;

            CellSizeReference = 0;
            //int everyNthSample = 1;
            //for (int s = 0; s < Cells.Length; s += everyNthSample)
            //{
            //    float dist = (Vertices[Cells[s][0]] - Vertices[Cells[s][1]]).LengthSquared();
            //    CellSizeReference = Math.Max(CellSizeReference, dist);
            //}



            _cellCenters = new VectorBuffer(Cells.Length, Vertices.VectorLength);
            for (int c = 0; c < Cells.Length; ++c)
            {
                
                _cellCenters[c] = (Vertices[Cells[c][0]] + Vertices[Cells[c][1]] + Vertices[Cells[c][2]] + Vertices[Cells[c][3]]) * 0.25f;

                //for (int v = 0; v < 4; ++v)
                CellSizeReference = Math.Max(CellSizeReference, (_cellCenters[c] - Vertices[Cells[c][0]]).LengthSquared());
            }

            CellSizeReference = (float)Math.Sqrt(CellSizeReference); // * 1.1f; // Allow for some error.
            Console.WriteLine("Maximal cell size found: " + CellSizeReference);

            _cellCenters.MinValue = new Vector(Vertices.MinValue);
            _cellCenters.MaxValue = new Vector(Vertices.MaxValue);

            Vector howOftenDoesCellSizeFitThere = _cellCenters.Extent / CellSizeReference * 0.5f;

            // Compute maximal level.
            // Setup Octree for fast access.
            Tree = new Octree(_cellCenters, maxNumVertices, maxDepth, howOftenDoesCellSizeFitThere.Min());
            

            watch.Stop();
            Console.WriteLine("Grid buildup took {0}m {1}s", (int)watch.Elapsed.TotalMinutes, watch.Elapsed.Seconds);
        }

        public Index[] GetAllSides()
        {
            List<Index> sides = new List<Index>(Cells.Length);

            for (int c = 0; c < Cells.Length; ++c)
            {
                // Dirty quickfix: Duplicate the first cells multiple times, so we don't need to deal with uninitialized tets.
                Index verts = Cells[c];
                if (verts == null)
                    continue;
                sides.Add(new Index(new int[] { verts[0], verts[1], verts[2] }));
                sides.Add(new Index(new int[] { verts[0], verts[2], verts[3] }));
                sides.Add(new Index(new int[] { verts[0], verts[1], verts[3] }));
                sides.Add(new Index(new int[] { verts[1], verts[2], verts[3] }));
            }

            return sides.ToArray();
        }

        public override FieldGrid Copy()
        {
            throw new NotImplementedException("Grid is so big I don't want to copy it.");
        }

        public override int NumAdjacentPoints()
        {
            return 4; // Constant withing tetraeders, though we don't know neighborhood of vertices themselves.
        }

        /// <summary>
        /// Append a time dimension.
        /// </summary>
        /// <param name="numTimeSlices"></param>
        /// <param name="timeStart"></param>
        /// <param name="timeStep"></param>
        /// <returns></returns>
        public override FieldGrid GetAsTimeGrid(int numTimeSlices, float timeStart, float timeStep)
        {
            return this;//new TetGrid(this);
        }

        /// <summary>
        /// Returns the adjacent grid point indices.
        /// Indices in ascending order.
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="indices"></param>
        /// <param name="weights"></param>
        public override Index FindAdjacentIndices(VectorRef pos, out VectorRef weights)
        {
            int cell = FindCell(pos, out weights);
            //Debug.Assert(cell >= 0, "Not in the grid.");
            return cell >= 0 ? new Index(Cells[cell]) : null;
        }

        public override bool InGrid(Vector position)
        {
            return true;
            //Debug.Assert(position.Length == Size.Length, "Trying to access " + Size.Length + "D field with " + position.Length + "D index.");

            //Vector bary;
            //return FindCell(position, out bary) >= 0;
        }

        static uint NUM_SAMPLES = 0;
        static uint NUM_UNSUCCESSFULL_SAMPLES = 0;
        static uint NUM_SAMPLE_OUTSIDE_LEAF = 0;
        static uint NUM_SAMPLES_NO_NEW_NEIGHBORS = 0;
        static uint NUM_SLIGHTLY_NEGATIVE = 0;
        static uint NUM_OUTSIDE = 0;

        public static void ShowSampleStatistics()
        {
            Console.WriteLine("{0} \tsamples.\n{1}% \tsampled neighbor nodes.\n\t{2}% \tof those did not actually sample neighbors\n\t{3}% of them are slightly negative\n\t{4}% \t of them not found at all\n{5} outside of bounding box.\nStabbing took {6}\nBary Determinants took {7}\n===============",
                NUM_SAMPLES, ((float)NUM_SAMPLE_OUTSIDE_LEAF) / NUM_SAMPLES * 100, ((float)NUM_SAMPLES_NO_NEW_NEIGHBORS) / NUM_SAMPLE_OUTSIDE_LEAF * 100, ((float)NUM_SLIGHTLY_NEGATIVE) / NUM_SAMPLE_OUTSIDE_LEAF * 100, ((float)NUM_UNSUCCESSFULL_SAMPLES) / NUM_SAMPLE_OUTSIDE_LEAF * 100, NUM_OUTSIDE,
                Octree.PROF_WATCH, PROF_BARY);
        }

        public static float MAX_NEGATIVE_BARY = 0.002f;

        private int FindCell(VectorRef pos, out VectorRef bary)
        {
            // Stab the tree.
            // Search through all cells that have a vertex in the stabbed leaf.
            Node leaf;
            var vertices = Tree.StabCell(pos, out leaf);
            NUM_SAMPLES++;

            //if (NUM_SAMPLES % 10 == 0)
            //    Console.WriteLine(NUM_SAMPLES);

            // Outside the octrees bounding box?
            if (leaf == null)
            {
                bary = null;
                NUM_OUTSIDE++;
                return -1;
            }

            int tet = FindInNode(vertices.GetData(Tree), pos, out bary);

            if (tet >= 0)
                return tet;

            NUM_SAMPLE_OUTSIDE_LEAF++;

            // Test the neighbor cells in a maximal radius around the center.
            tet = Tree.FindNeighborNodes(this, pos, leaf, out bary);

            if (tet >= 0)
                return tet;

            //if (bestNegativeTet >= 0 || bestNegativeNeighborTet >= 0)
            //{
            //    NUM_SLIGHTLY_NEGATIVE++;
            //    if (neighborBary.AbsSumNegatives() < bary.AbsSumNegatives())
            //    {
            //        bary = neighborBary;
            //        Debug.Assert(bary.AbsSumNegatives() < MAX_NEGATIVE_BARY);
            //        return bestNegativeTet;
            //    }

            //    Debug.Assert(bary.AbsSumNegatives() < MAX_NEGATIVE_BARY);
            //    return bestNegativeNeighborTet;
            //}

            NUM_UNSUCCESSFULL_SAMPLES++;
            return -1;
        }

        public int FindInNode(CellData tets, VectorRef pos,/* float maxNegativeBary,*/ out VectorRef bary/*, out int negativeTet*/)
        {
            // Squared distance maximal 3 times as high as a random tet edge length.
//            float distEps = CellSizeReference * CellSizeReference * 9;

            bary = null;

            //Vector bestNegBary = null;
            //negativeTet = -1;
            //float bestBaryDist = 1;

            // Test whether inside.
            foreach (int tet in tets)
            {
                //if ((_cellCenters[tet] - pos).LengthSquared() > distEps)
                //    continue;

                bary = ToBaryCoord(tet, pos);

                if (bary != null && bary.IsPositive())
                {
                    return tet;
                }
                //float baryDiff = bary.AbsSumNegatives();

                //if (baryDiff < bestBaryDist)
                //{
                //    bestBaryDist = baryDiff;
                //    bestNegBary = new Vector(bary);
                //    negativeTet = tet;
                //}

            }

            //if (bestBaryDist < maxNegativeBary)
            //{
            //    bary = bestNegBary;
            //}
            return -1;
        }
        private int FindInNode(List<CellData> data, VectorRef pos,/* float maxNegativeBary, */out VectorRef bary/*, out int negativeTet*/)
        {
            // Squared distance maximal 3 times as high as a random tet edge length.
            float distEps = CellSizeReference * CellSizeReference * 9;
            bary = null;
            //bary = new Vector(-1, 4);
            //Vector bestNegBary = null;
            //negativeTet = -1;
            //float bestBaryDist = 1;

            // Test whether inside.
            foreach (CellData tets in data)
                foreach (int tet in tets)
                {
                    if ((_cellCenters[tet] - pos).LengthSquared() > distEps)
                        continue;

                    bary = ToBaryCoord(tet, pos);
                    if (bary.IsPositive())
                    {
                        return tet;
                    }

                    //float baryDiff = bary.AbsSumNegatives();

                    //if (baryDiff < bestBaryDist)
                    //{
                    //    bestBaryDist = baryDiff;
                    //    bestNegBary = new Vector(bary);
                    //    negativeTet = tet;
                    //}
                }

            //if (bestBaryDist < maxNegativeBary)
            //{
            //    bary = bestNegBary;
            //}
            return -1;
        }

        public Vector OldToBaryCoord(int cell, VectorRef worldPos)
        {
            Debug.Assert(worldPos.Length == Vertices.VectorLength);
            SquareMatrix tet = new SquareMatrix(3);
            VectorRef origin = Vertices[Cells[cell][0]];
            for (int i = 0; i < 3; ++i)
            {
                tet[i] = Vertices[Cells[cell][i + 1]] - origin;
            }

            Vector result = tet.Inverse() * ((worldPos - origin));
            result = new Vector(new float[] { 1f - result.Sum(), result[0], result[1], result[2] });

            return result;
        }
        static Stopwatch PROF_BARY = new Stopwatch();
        public Vector ToBaryCoord(int cell, VectorRef worldPos)
        {
            PROF_BARY.Start();
            Debug.Assert(worldPos.Length == Vertices.VectorLength);
            SquareMatrix tet = new SquareMatrix(4);
            for (int i = 0; i < 4; ++i)
            {
                tet[i] = VectorRef.ToUnsteady(Vertices[Cells[cell][i]]);
            }

            //Console.WriteLine("===========\nTetrahedron matrix {0}", tet);

            float d0 = tet.Determinant();

            Vector bary = new Vector(-42, 4);

            // Go over all corner points and exchange them with the sample position.
            // If sign of determinant is the same as of the original, cube, the point is on the same side.
            for (int i = 0; i < 4; ++i)
            {
                SquareMatrix mi = new SquareMatrix(tet);
                mi[i] = VectorRef.ToUnsteady(worldPos);
                bary[i] = mi.Determinant() / d0;
                if (bary[i] <= 0)
                {
                    PROF_BARY.Stop();
                    return null;
                }
            }
            float barySum = bary.Sum();
            //float eps = 0.01f;
            //Console.WriteLine("===========\nPosition {0}\nBarycentric Coordinate {1}", worldPos, bary);
            PROF_BARY.Stop();
            return bary;
        }


        /// <summary>
        /// Binary search for the last point inside the domain.
        /// </summary>
        /// <param name="pos">The last valid position on the inside.</param>
        /// <param name="outsidePos">First position found outside.</param>
        /// <returns></returns>
        public override Vector CutToBorder(VectorField field, VectorRef pos, VectorRef outsidePos)
        {
            float eps = CellSizeReference / 1000;
            Vector dir = outsidePos - pos;
            float dirLength = dir.LengthEuclidean();
            float dirPercentage = 0.5f;
            float step = 0.25f;

            VectorRef outBary;
            int lastWorkingCell = -1;

            while (dirLength * step > eps)
            {
                int cell = lastWorkingCell;
                Vector samplePos = pos + dir * dirPercentage;
                if (cell < 0 || !ToBaryCoord(cell, samplePos).IsPositive())
                    cell = FindCell(samplePos, out outBary);
                lastWorkingCell = (cell >= 0) ? cell : lastWorkingCell;
                dirPercentage += (cell >= 0) ? step : -step;
                step *= 0.5f;
            }

            return pos + dir * dirPercentage;
        }

        #region DebugRendering

        //public LineSet GetWireframe()
        //{
        //    Line[] lines = new Line[Indices.Length];

        //    for (int l = 0; l < Indices.Length; ++l)
        //    {
        //        lines[l] = new Line(NumCorners);
        //        for (int v = 0; v < NumCorners; ++v)
        //        {
        //            lines[l].Positions[v] = (SlimDX.Vector3) Vertices[Indices[l][v]];
        //        }
        //    }

        //    return new LineSet(lines);
        //}

        public PointSet<Point> SampleTest(VectorField data, int vertsPerSide = 100)
        {
            data.Data.ExtractMinMax();

            Stopwatch watch = new Stopwatch();
            watch.Start();

            List<Point> verts = new List<Point>(vertsPerSide * vertsPerSide * vertsPerSide / 5);
            Vector extent = Vertices.MaxValue - Vertices.MinValue;

            // DEBUGGGGGGGGGGGGGGGGGGGG
            // 1739065 : {[79, 22, 11]}
            int realTet = 1739065;
            int permIndex = Tree.GetTetPermutationPosition(realTet);
            VectorRef tetPos = Tree.Vertices[realTet];
            Console.WriteLine("============\nTet Permutation Index {0}\nTet Position {1}\nTet Position in Grid Coords {2}\n============", permIndex, tetPos, Tree.ToGridPosition(tetPos));
            Vector posT = Vertices.MinValue +
                            new Vector(new float[] {
                                       ((float)79) / vertsPerSide * extent[0],
                                       ((float)22) / vertsPerSide * extent[1],
                                       ((float)11) / vertsPerSide * extent[2] });
            Console.WriteLine( "\n===========\nPosition {0}, in Grid {1}", posT, Tree.ToGridPosition(posT));

            Vector tmpDist = Tree.ToGridPosition(posT) - Tree.ToGridPosition(tetPos);
            Console.WriteLine("Position minus Tet Center Position: {0}\n\tIn Grid Positions: {1}\n\tIndeed, {0} in Grid Distance is {2}",
                              posT - tetPos, tmpDist, tmpDist * Tree.MaxLeafSize);

            //Vector baryT = ToBaryCoord(realTet, posT);
            //Console.WriteLine("===========\nBary Position {0}", baryT);
            Vector sampleT = Sample(data, posT);
            if (sampleT != null)
                Console.WriteLine("Test Case Works T_T");
            Console.WriteLine("Test case took {0}m {1}s {2}", (int)watch.Elapsed.TotalMinutes, watch.Elapsed.Seconds, watch.Elapsed.Milliseconds);
            // \DEBUGGGGGGGGGGGGGGGGGGGG
            ShowSampleStatistics();
            // Default-false.
            //bool[,,] notFound = new bool[vertsPerSide, vertsPerSide, vertsPerSide];
            int numNotFound = 0;

            for (int z = 0; z < vertsPerSide; ++z)
            {
                for (int y = 0; y < vertsPerSide; ++y)
                        for (int x = 0; x < vertsPerSide; ++x)
                        {
                        Vector pos = Vertices.MinValue +
                            new Vector(new float[] {
                                       ((float)x) / vertsPerSide * extent[0],
                                       ((float)y) / vertsPerSide * extent[1],
                                       ((float)z) / vertsPerSide * extent[2] });

                        Vector sample = Sample(data, pos);
                        if (sample == null)
                        {
                            //notFound[x, y, z] = true;
                            numNotFound++;
                        }
                        else
                        {
                            float color = ((sample - data.Data.MinValue) / (data.Data.MaxValue - data.Data.MinValue))[0];
                            verts.Add(new Point((Vector3)pos) { Radius = 0.01f, Color = new Vector3(color) });
                        }
                        Console.WriteLine("{0} / {1} sampled", x + vertsPerSide * y + z * vertsPerSide * vertsPerSide, vertsPerSide * vertsPerSide * vertsPerSide);
                    }
                Console.WriteLine("{0}% Sampling Complete", (z * 100) / vertsPerSide);
                ShowSampleStatistics();
            }

            Console.WriteLine("{0} Samples not found\n===========", numNotFound);
            Console.WriteLine("Sampling done.\nStatistics before:\n==================");
            ShowSampleStatistics();
            //try
            //{
            //    //'System.IndexOutOfRangeException' in FlowSharp.exe
            //Util.FloodFill(notFound, new Index(0, 3));
            //} catch(Exception e)
            //{
            //    Console.WriteLine(e.InnerException);
            //}
            //Console.WriteLine("Flood Fill Done\n===============");

            //foreach (GridIndex offsetGI in new GridIndex(new Index(vertsPerSide, 3)))
            //{
            //    Index idx = offsetGI;
            //    if (!notFound[idx[0], idx[1], idx[2]])
            //        continue;

            //    Vector pos = Vertices.MinValue +
            //                new Vector(new float[] {
            //                           ((float)idx[0]) / vertsPerSide * extent[0],
            //                           ((float)idx[1]) / vertsPerSide * extent[1],
            //                           ((float)idx[2]) / vertsPerSide * extent[2] });

            //    float color = ((float)idx[2]) / vertsPerSide; // ((sample - data.Data.MinValue) / (data.Data.MaxValue - data.Data.MinValue))[0];
            //    verts.Add(new Point((Vector3)pos) { Radius = 0.01f, Color = new Vector3(color) });
            //}

            watch.Stop();
            Console.WriteLine("Grid stabbing with {0} unsuccessfull samples took {1}m {2}s", verts.Count, (int)watch.Elapsed.TotalMinutes, watch.Elapsed.Seconds);

            return new PointSet<Point>(verts.ToArray());
        }

        public PointSet<Point> GetVertices()
        {
            Point[] verts = new Point[Vertices.Length];
            for (int p = 0; p < Vertices.Length; ++p)
            {
                verts[p] = new Point((Vector3)Vertices[p]) { Color = (Vector3)Vertices[p], Radius = 0.001f };
            }

            return new PointSet<Point>(verts);
        }
        #endregion DebugRendering
    }

    //struct Tet
    //{
    //    /// <summary>
    //    /// Indices referencing the vertices in the containing grid.
    //    /// </summary>
    //    public Index VertexIndices;

    //    /// <summary>
    //    /// Create a Tetraeder.
    //    /// </summary>
    //    /// <param name="verts">Vertex indices [4]</param>
    //    public Tet(Index verts)
    //    {
    //        Debug.Assert(verts.Length == 4, "Tetraeders have exactly 4 corners.");
    //        VertexIndices = verts;
    //    }
    //    public Vector ToBaryCoord(TetTreeGrid grid, Vector worldPos)
    //    {
    //        Matrix tet = new Matrix();
    //        for (int c = 0; c < 4; ++c)
    //        {
    //            tet.set_Columns(c, (Vector4)grid.Vertices[VertexIndices[c]]);
    //        }

    //        tet.Invert();
    //        Vector4 result = Vector4.Transform((Vector4)worldPos, tet);
    //        return new Vector(result);

    //    }
    //}
}
