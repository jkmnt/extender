using System;
using System.Collections;
using System.Collections.Generic;

using CamBam.UI;
using CamBam.CAD;
using CamBam.Geom;
using CamBam;


namespace Extender
{
    class Trimmer
    {
        private double _tolerance
        {
            get { return CamBamConfig.Defaults.GeneralTolerance; }
        }

        private CamBamUI _ui
        {
            get { return CamBamUI.MainUI; }
        }


        private List<Point2F> collect_intersections(List<Polyline> boundaries, Polyline line)
        {
            List<Point2F> insects = new List<Point2F>();
            foreach (Polyline p in boundaries)
                insects.AddRange(Polyline.PolylineIntersections(line, p, _tolerance));

            return insects;
        }

        private Polyline trim_start(List<Point2F> insects, Polyline line)
        {
            Polyline candidate = null;

            double minlen = double.MaxValue;

            foreach (Point2F pt in insects)
            {
                Polyline[] parts = line.SplitAtPoint(pt, _tolerance);

                // just for now: ignore all splits except the well defined
                if (parts.Length != 2)
                    continue;

                double len = parts[0].GetPerimeter();

                if (len < minlen)
                {
                    minlen = len;
                    candidate = parts[1];
                }
            }

            return candidate;
        }

        private Polyline trim_end(List<Point2F> insects, Polyline line)
        {
            Polyline candidate = null;

            double minlen = double.MaxValue;

            foreach (Point2F pt in insects)
            {
                Polyline[] parts = line.SplitAtPoint(pt, _tolerance);

                // just for now: ignore all splits except the well defined
                if (parts.Length != 2)
                    continue;

                double len = parts[1].GetPerimeter();

                if (len < minlen)
                {
                    minlen = len;
                    candidate = parts[0];
                }
            }

            return candidate;
        }

        private List<Polyline> preprocess_boundaries(Entity[] boundaries, List<Entity> except)
        {
            List<Polyline> bounds = new List<Polyline>();

            foreach (Entity e in boundaries)
            {
                if (except.Contains(e))
                    continue;

                IToPolylines b = (IToPolylines) e;

                try
                {
                    if (b.CanConvertToPolylines)
                        bounds.AddRange(b.ConvertToPolylines(false));
                }
                catch
                {
                    Host.log("Failed to process boundary");
                }
            }

            return bounds;
        }

        private double min_dist(List<Point2F> cloud, Point2F pt)
        {
            double min = double.MaxValue;

            foreach (Point2F p in cloud)
            {
                double dist = p.DistanceTo(pt);
                if (dist < min)
                    min = dist;
            }

            return min;
        }

        private List<Polyline> exec_trim(Polyline victim, List<Polyline> bounds, Point2F pickpoint, double pick_tolerance)
        {
            Vector2F normal = Vector2F.Undefined;
            int nearest_seg = 0;
            Point2F snap = (Point2F)victim.GetNearestPoint(pickpoint, ref normal, ref nearest_seg, true);
            if (snap.DistanceTo(pickpoint) > pick_tolerance)
                return null;

            Polyline[] parts = victim.SplitAtPoint((Point2F)snap, _tolerance);
            if (parts.Length == 0)
                return null;

            Polyline seg0 = null;
            Polyline seg1 = null;

            if (parts.Length == 1)
            {
                if (! victim.Closed)   // pickpoint is too close to the end or something go wrong
                    return null;

                List<Point2F> insects = collect_intersections(bounds, parts[0]);

                if (insects.Count < 2)
                    return null;

                if (min_dist(insects, pickpoint) < pick_tolerance)  // intersection is too close to the pick point
                    return null;

                seg0 = trim_start(insects, parts[0]);
                if (seg0 != null)
                    seg0 = trim_end(insects, seg0);
            }
            else
            {
                if (victim.Closed)
                {
                    Host.warn("split of closed polyline resulted in several parts, ignoring");
                    return null;
                }

                List<Point2F> insects0 = collect_intersections(bounds, parts[0]);
                List<Point2F> insects1 = collect_intersections(bounds, parts[1]);

                if (insects0.Count == 0 && insects1.Count == 0) // no intersections at all
                {
                    return null;
                }

                if (insects0.Count != 0)
                {
                    if (min_dist(insects0, pickpoint) < pick_tolerance)
                        return null;
                    seg0 = trim_end(insects0, parts[0]);
                }

                if (insects1.Count != 0)
                {
                    if (min_dist(insects1, pickpoint) < pick_tolerance)
                        return null;
                    seg1 = trim_start(insects1, parts[1]);
                }
            }

            List<Polyline> result = new List<Polyline>();

            if (seg0 != null)
                result.Add(seg0);
            if (seg1 != null)
                result.Add(seg1);

            return result;
        }

        private void replace_poly(Polyline dst, Polyline src)
        {
            dst.Points.Clear();
            foreach (PolylineItem p in src.Points)
                dst.Add(p.Point, p.Bulge);
            dst.Closed = false;
            dst.Update();
        }

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

        private Line poly2line(Polyline p)
        {
            return (Line)(p.ToPrimitives()[0]);
        }

        private Arc poly2arc(Polyline p)
        {
            return (Arc)(p.ToPrimitives()[0]);
        }

        private void insert_new_object(Polyline obj, Entity orig)
        {
            if (orig is Arc)
                _ui.ActiveView.CADFile.ActiveLayer.Entities.Add(poly2arc(obj));
            else if (orig is Line)
                _ui.ActiveView.CADFile.ActiveLayer.Entities.Add(poly2line(obj));
            else
                _ui.ActiveView.CADFile.ActiveLayer.Entities.Add(obj);
        }

        public void Run(Pickinfo pinfo, Entity[] boundaries)
        {
            Entity orig = pinfo.targ;


            List<Entity> except = new List<Entity>();
            except.Add(orig);
            List<Polyline> bounds =  preprocess_boundaries(boundaries, except);

            if (bounds.Count == 0)
                return;

            Polyline victim;

            if (orig is Polyline)
                victim = (Polyline)orig;
            else if (orig is Arc)
                victim = ((Arc)orig).ToPolyline();
            else if (orig is Circle)
                victim = ((Circle)orig).ToPolyline();
            else if (orig is Line)
                victim = ((Line)orig).ToPolyline();
            else
                return;

            List<Polyline> result = exec_trim(victim, bounds, pinfo.pickpoint, pinfo.tolerance);

            if (result == null || result.Count == 0)
                return;

            _ui.ActiveView.SuspendRefresh();

            _ui.ActiveView.CADFile.Modified = true;
            _ui.UndoBuffer.AddUndoPoint("Extender Plugin");

            // replace original entry inplace if there is a single result.
            // circle is an exception - it would be replaced by polyline
            if (result.Count == 1 && ! (orig is Circle))
            {
                _ui.UndoBuffer.Add(orig);

                if (orig is Polyline)
                    replace_poly((Polyline)orig, result[0]);
                else if (orig is Arc)
                    replace_arc((Arc)orig, result[0]);
                else if (orig is Line)
                    replace_line((Line)orig, result[0]);
            }
            else
            {
                // create new objects if orig can't be replaced

                _ui.ActiveView.CADFile.EnsureActiveLayer(true);
                _ui.UndoBuffer.Add(_ui.ActiveView.CADFile.ActiveLayer.Entities);

                insert_new_object(result[0], orig);

                if (result.Count > 1)
                    insert_new_object(result[1], orig);

                _ui.ActiveView.CADFile.ActiveLayer.Entities.Remove(orig);
            }

            _ui.ActiveView.Selection.RefreshExtrema();
            _ui.ActiveView.ResumeRefresh();
            _ui.ObjectProperties.Refresh();
            _ui.ActiveView.UpdateViewport();
        }

    }

}
