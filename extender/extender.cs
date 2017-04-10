using System;
using System.Collections;
using System.Collections.Generic;

using CamBam.UI;
using CamBam.CAD;
using CamBam.Geom;
using CamBam;


namespace Extender
{
    class Dist_comparer : IComparer<Point2F>
    {
        Point2F _origin;
        public Dist_comparer(Point2F origin)
        {
            _origin = origin;
        }

        double dist_to_origin(Point2F p)
        {
            return Point2F.Distance(p, _origin);
        }

        public int Compare(Point2F a, Point2F b)
        {
            double d0 = dist_to_origin(a);
            double d1 = dist_to_origin(b);
            if (d0 < d1)
                return -1;
            if (d0 > d1)
                return 1;
            return 0;
        }
    }

    class Sweep_comparer : IComparer<Point2F>
    {
        Point2F _origin;
        Point2F _center;
        RotationDirection _dir;
        public Sweep_comparer(Point2F origin, Point2F center, RotationDirection dir)
        {
            _origin = origin;
            _center = center;
            _dir = dir;
        }

        double abs_sweep_to_origin(Point2F p)
        {
            return Math.Abs(new Arc2F(_center, _origin, p, _dir).Sweep);
        }

        public int Compare(Point2F a, Point2F b)
        {
            double d0 = abs_sweep_to_origin(a);
            double d1 = abs_sweep_to_origin(b);
            if (d0 < d1)
                return -1;
            if (d0 > d1)
                return 1;
            return 0;
        }
    }

    class Extender
    {
        private double _tolerance
        {
            get { return CamBamConfig.Defaults.GeneralTolerance; }
        }

        private CamBamUI _ui
        {
            get { return CamBamUI.MainUI; }
        }

        bool samepoint(Point3F p0, Point3F p1)  { return p0.X == p1.X && p0.Y == p1.Y;}
        bool samepoint(Point2F p0, Point2F p1)  { return p0.X == p1.X && p0.Y == p1.Y;}
        bool samepoint(Point3F p0, Point2F p1)  { return p0.X == p1.X && p0.Y == p1.Y;}
        bool samepoint(Point2F p0, Point3F p1)  { return p0.X == p1.X && p0.Y == p1.Y;}

        Point3F p3f(Point2F p)  {  return new Point3F(p.X, p.Y, 0);}
        Point2F p2f(Point3F p)  {  return new Point2F(p.X, p.Y);}

        Point2F[] get_global_bb()
        {
            Point3F min = Point3F.Undefined;
            Point3F max = Point3F.Undefined;
            Point3F mn = new Point3F();
            Point3F mx = new Point3F();

            foreach (Layer layer in _ui.ActiveView.CADFile.Layers)
            {
                foreach (Entity e in layer.Entities)
                {
                    e.GetExtrema(ref mn, ref mx);
                    if (min.IsUndefined)
                    {
                        min = mn;
                        max = mx;
                    }
                    else
                    {
                        if (mn.X < min.X) min.X = mn.X;
                        if (mn.Y < min.Y) min.Y = mn.Y;
                        if (mx.X > max.X) max.X = mx.X;
                        if (mx.Y > max.Y) max.Y = mx.Y;
                    }
                }
            }
            return new Point2F[] {new Point2F(min.X, min.Y), new Point2F(max.X, max.Y)};
        }

        void sort_points_by_distance(List<Point2F> points, Point2F origin)
        {
            points.Sort(new Dist_comparer(origin));            
        }

        void sort_points_by_sweep(List<Point2F> points, Point2F origin, Point2F center, RotationDirection dir)
        {
            points.Sort(new Sweep_comparer(origin, center, dir));
        }

        List<Point2F> collect_intersections(Entity[] boundaries, Polyline line, Point2F origin, Point2F limit)
        {
            List<Polyline> polys = new List<Polyline>();
            foreach (IToPolylines b in boundaries)
            {
                try
                {
                    if (b.CanConvertToPolylines)
                        polys.AddRange(b.ConvertToPolylines(false));
                }
                catch
                {
                    Host.log("Failed to process boundary");
                }
            }
            List<Point2F> insects = new List<Point2F>();
            foreach (Polyline p in polys)
            {
                insects.AddRange(Polyline.PolylineIntersections(line, p, _tolerance));
            }
            List<Point2F> true_insects = new List<Point2F>();
            foreach (Point2F p in insects)
            {
                if (Point2F.Distance(p, origin) < _tolerance)
                    continue;
                if ((! limit.IsUndefined) && Point2F.Distance(p, limit) < _tolerance)
                    continue;
                true_insects.Add(p);
            }
            return true_insects;
        }

        bool find_extended_line(Entity[] boundaries, Line2F segment, ref Line2F result)
        {
            // craft line with a length equal to a diagonal of the global bounding box (including initial segment). This line is guaranteed
            // to penetrate all workspace.
            Polyline ray = new Polyline();
            ray.Add(segment, 0);
            Point2F[] bb = get_global_bb();
            if (segment.Length() < _tolerance)
                return false;
            double scale = Point2F.Distance(bb[0], bb[1]) / segment.Length();
            Matrix4x4F m = new Matrix4x4F();
            m.Translate(-segment.p1.X, -segment.p1.Y, 0);
            m.Scale(scale, scale, 0);
            m.Translate(segment.p2.X, segment.p2.Y, 0);
            ray.ApplyTransformation(m);
            List<Point2F> new_insects = collect_intersections(boundaries, ray, segment.p2, Point2F.Undefined);
            sort_points_by_distance(new_insects, segment.p2);
            if (new_insects.Count < 1)
                return false;
            result = new Line2F(segment.p1, new_insects[0]);
            return true;
        }

        bool find_extended_arc(Entity[] boundaries, Arc2F segment, ref Arc2F result)
        {
            // craft arc completing the circe
            Polyline arc = new Polyline();
            arc.Add(new Arc2F(segment.Center, segment.P2, segment.P1, segment.Direction), 0);
            List<Point2F> new_insects = collect_intersections(boundaries, arc, segment.P2, segment.P1);
            sort_points_by_sweep(new_insects, segment.P2, segment.Center, segment.Direction);
            if (new_insects.Count < 1)
                return false;
            result = new Arc2F(segment.Center, segment.P1, new_insects[0], segment.Direction);
            return true;
        }

        void replace_poly(Polyline dst, Polyline src)
        {
            dst.Points.Clear();
            foreach (PolylineItem p in src.Points)
                dst.Add(p.Point, p.Bulge);
            dst.Update();
        }

        // don't we get a stuck reference to old line here ?
        void replace_line(Line dst, Polyline src)
        {
            Line line = (Line)(src.ToPrimitives()[0]);
            dst.Points[0] = line.Points[0];
            dst.Points[1] = line.Points[1];
        }

        void replace_arc(Arc dst, Polyline src)
        {
            Arc arc = (Arc)(src.ToPrimitives()[0]);
            dst.Point = arc.Point;
            dst.Start = arc.Start;
            dst.Sweep = arc.Sweep;
            dst.Radius = arc.Radius;
            dst.Update();
        }

        Polyline make_extended_poly(Polyline victim, object segment, bool extend_from_beginning, Entity[] boundaries)
        {
            if (segment is Line2F)
            {
                Line2F seg = (Line2F)segment;
                if (extend_from_beginning)   // reverse line if pickpoint is closer to the beginning
                    seg = new Line2F(seg.p2, seg.p1);

                Line2F replacement = new Line2F();
                if (! find_extended_line(boundaries, seg, ref replacement))
                    return null;
                Polyline result = new Polyline();
                foreach(PolylineItem p in victim.Points)
                {
                    if (samepoint(p.Point, seg.p2))
                        result.Add(p3f(replacement.p2), p.Bulge);
                    else
                        result.Add(p.Point, p.Bulge);
                }
                return result;
            }

            if (segment is Arc2F)
            {
                Arc2F seg = (Arc2F)segment;
                if (extend_from_beginning)  // reverse arc if pickpoint is closer the to beginning
                    seg = new Arc2F(seg.Center, seg.P2, seg.P1, seg.Direction == RotationDirection.CW ? RotationDirection.CCW : RotationDirection.CW);
                Arc2F replacement = new Arc2F();
                if (! find_extended_arc(boundaries, seg, ref replacement))
                    return null;
                Polyline result = new Polyline();
                if (! extend_from_beginning)
                {
                    foreach(PolylineItem p in victim.Points)
                    {
                        if (samepoint(p.Point, seg.P1))
                            result.Add(p.Point, replacement.Bulge);
                        else if (samepoint(p.Point, seg.P2))
                            result.Add(p3f(replacement.P2), p.Bulge);
                        else
                            result.Add(p.Point, p.Bulge);
                    }
                }
                else
                {
                    foreach(PolylineItem p in victim.Points)
                    {
                        if (samepoint(p.Point, seg.P2))
                            result.Add(p3f(replacement.P2), -replacement.Bulge);
                        else
                            result.Add(p.Point, p.Bulge);
                    }
                }
                return result;
            }

            // assert here
            return null;
        }

        Point2F[] seg_endpoints(object obj)
        {
            if (obj is Line2F)
                return new Point2F[] {((Line2F)obj).p1, ((Line2F)obj).p2};
            return new Point2F[] {((Arc2F)obj).P1, ((Arc2F)obj).P2};
        }

        public void Run(Pickinfo pinfo, Entity[] bounds)
        {
            Entity orig = pinfo.targ;

            Polyline victim;

            if (orig is Polyline)
                victim = (Polyline)orig;
            else if (orig is Arc)
                victim = ((Arc)orig).ToPolyline();
            else if (orig is Line)
                victim = ((Line)orig).ToPolyline();
            else
                return;

            int segidx = victim.NearestSegment(pinfo.pickpoint, pinfo.tolerance);
            if (segidx < 0)
                return;

            object segment = victim.GetSegment(segidx);
            bool extend_from_beginning = Point2F.Distance(pinfo.pickpoint, seg_endpoints(segment)[0]) <  Point2F.Distance(pinfo.pickpoint, seg_endpoints(segment)[1]);

            Polyline new_poly = make_extended_poly(victim, segment, extend_from_beginning, bounds);

            if (new_poly == null)
                return;

            _ui.ActiveView.CADFile.Modified = true;
            _ui.UndoBuffer.AddUndoPoint("Extender Plugin");
            _ui.UndoBuffer.Add(orig);

            if (orig is Polyline)
                replace_poly((Polyline)orig, new_poly);
            else if (orig is Line)
                replace_line((Line)orig, new_poly);
            else if (orig is Arc)
                replace_arc((Arc)orig, new_poly);

            _ui.ActiveView.UpdateViewport();
        }
    }
}
