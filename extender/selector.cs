using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

using CamBam.UI;
using CamBam.CAD;
using CamBam.Geom;
using CamBam.Util;

namespace Extender
{
    class Pickinfo
    {
        public Entity targ;
        public Point2F pickpoint;
        public double tolerance;
        public bool should_trim;
    };

    public class PickObjectEditMode : EditMode
    {
        Point cursor = new Point(0, 0);
        double pixel_tolerance = 3; // do not change
        bool _is_shift_pressed = false;

        Point3F to_drawing_point(Point pt)
        {
            return this._ActiveView.ScreenToDrawing(new PointF((float)pt.X, (float)pt.Y));;
        }

        public PickObjectEditMode(ICADView iv) : base(iv)
        {
            this.MarkFileModified = false;
            if (! this.CheckForLayer(true))
            {
                base.ReturnStatus = EditMode.ReturnStatusCode.Error;
                return;
            }
        }

        public override bool Repeat()
        {
            base.ReturnStatus = EditMode.ReturnStatusCode.Running;
            return true;
        }

        public override void OnPaint(ICADView iv, Display3D d3d)
        {
            if (base.ReturnStatus != EditMode.ReturnStatusCode.Running) return;

            Layer layer = this._ActiveView.CADFile.EnsureActiveLayer(true);
            d3d.ModelTransform = Matrix4x4F.Identity;
            if (_is_shift_pressed)
                d3d.LineColor = Color.Blue;
            else
                d3d.LineColor = Color.Red;
            //d3d.LineColor = layer.Color;
            d3d.DrawPoint(to_drawing_point(this.cursor), (float)this.pixel_tolerance);
            base.OnPaint(iv, d3d);
        }

        public override bool OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this._ActiveView.RepaintEditMode();
                this.cursor = e.Location;
                Entity tgt = this._ActiveView.GetPrimitiveAtPoint(this.cursor);
                if (tgt == null)
                    return true;

                Point3F pickpoint = this._ActiveView.ScreenToDrawing(this.cursor);

                Pickinfo result = new Pickinfo();
                result.targ = tgt;
                result.pickpoint = new Point2F(pickpoint.X, pickpoint.Y);
                result.tolerance = Point3F.Distance(to_drawing_point(new Point(0, 0)), to_drawing_point(new Point(0, (int)pixel_tolerance)));
                result.should_trim = _is_shift_pressed;

                base.ReturnValue = result;
                this.ReturnOK();
                return true;
            }
            else if (e.Button == MouseButtons.Middle)
            {
                this.ReturnCancel();
                return true;
            }
            return false;
        }

        public override bool OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Shift)
                _is_shift_pressed = true;
            else
                _is_shift_pressed = false;

            this._ActiveView.RepaintEditMode();

            if (e.KeyCode == Keys.Return || e.KeyCode == Keys.Escape)
            {
                this.ReturnCancel();
                return true;
            }
            return false;
        }

        public override bool OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Shift)
                _is_shift_pressed = true;
            else
                _is_shift_pressed = false;

            this._ActiveView.RepaintEditMode();

            return base.OnKeyUp(sender, e);
        }

        public override bool OnMouseMove(object sender, MouseEventArgs e)
        {
            this.cursor = e.Location;
            this._ActiveView.RepaintEditMode();
            return base.OnMouseMove(sender, e);
        }
    }

    public class Selector
    {
        CamBamUI _ui;
        EditMode _em;

        private Entity[] get_filtered_selection()
        {
            List<Entity> valid_selections = new List<Entity>();

            foreach (Entity b in _ui.ActiveView.SelectedEntities)
            {
                if (b is Polyline || b is PolyRectangle || b is Line || b is Circle || b is MText || b is Spline || b is Arc || b is CamBam.CAD.Region)
                    valid_selections.Add(b);
            }

            return valid_selections.ToArray();
        }

        private void update_selection(Entity[] selection)
        {
            _ui.ActiveView.SelectObjects(selection);
        }

        private void on_object_pick(object sender, EventArgs args)
        {
            Entity[] selection = get_filtered_selection();

            if (selection.Length > 0)
            {
                update_selection(selection);    // display filtered result

                Pickinfo pi = (Pickinfo)_em.ReturnValue;
                if (pi.should_trim)
                {
                    Trimmer trimmer = new Trimmer();
                    trimmer.Run(pi, selection);
                }
                else
                {
                    Extender extender = new Extender();
                    extender.Run(pi, selection);
                }
            }

            _em.Repeat();
            _ui.ActiveView.RepaintEditMode();
        }

        private void on_selection_pick(object sender, EventArgs args)
        {
            Entity[] selection = get_filtered_selection();
            if (selection.Length < 1)
            {
                Host.log(TextTranslation.Translate("No valid boundaries selected"));
                return;
            }
            update_selection(selection);

            // Replace pick handler for picking extendable object
            _em = new PickObjectEditMode(_ui.ActiveView);
            _em.Prompt = TextTranslation.Translate("Click object to extend; Shift-Click to trim; press ESC to exit");
            _em.OnReturnOK += on_object_pick;
            _ui.ActiveView.SetEditMode(_em, true);
            _ui.ActiveView.RepaintEditMode();
        }

        public Selector(CamBamUI ui)
        {
            _ui = ui;
        }

        public void Run()
        {
            if (_ui.ActiveView.CurrentEditMode != null) // busy
                return;

            _em = new EntitySelectEditMode(_ui.ActiveView);
            _em.Prompt = TextTranslation.Translate("Select boundaries to extend to / trim by, then press Enter or ESC to cancel");
            _em.OnReturnOK += on_selection_pick;
            _ui.ActiveView.SetEditMode(_em);
            _ui.ActiveView.RepaintEditMode();
        }
    }
}