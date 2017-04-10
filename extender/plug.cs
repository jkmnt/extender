﻿using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

using CamBam;
using CamBam.UI;
using CamBam.CAD;


namespace Extender
{
    public class Host
    {
        static public void log(string s, params object[] args)
        {
            ThisApplication.AddLogMessage(s, args);
        }
        static public void warn(string s, params object[] args)
        {
            ThisApplication.AddLogMessage("Warning: " + s, args);
        }
        static public void err(string s, params object[] args)
        {
            ThisApplication.AddLogMessage("Error: " + s, args);
        }
        static public void msg(string s, params object[] args)
        {
            ThisApplication.MsgBox(String.Format(s, args));
        }
        static public void sleep(int ms)
        {
            System.Threading.Thread.Sleep(ms);
            System.Windows.Forms.Application.DoEvents();
        }
    }


    public class Extender_plugin
    {
        static Selector selector;

        static void popup_handler(object sender, EventArgs e)
        {
            selector.Run();
        }

        static void on_load(object sender, EventArgs e)
        {
            ToolStripPanel tsp = null;
            foreach (Control c in ThisApplication.TopWindow.Controls)
            {
                if (!(c is ToolStripContainer))
                    continue;
                tsp = ((ToolStripContainer)c).TopToolStripPanel;
                break;
            }

            ToolStrip ts = new ToolStrip();
            ts.Tag = "modify";

            ToolStripItem ti = ts.Items.Add(null, Properties.Resources.toolbar_icon, popup_handler);
            ti.ToolTipText = "Extender";
            
            // since controls layed in the reverse order, attach new toolstrip to the right edgde of the rightmost existing toolstrip.
            // extra dirty, but ... ok
            tsp.Join(ts, tsp.Controls[0].Right, 0);
        }

        public static void InitPlugin(CamBamUI ui)
        {
            ToolStripMenuItem popup = new ToolStripMenuItem("Extender");
            popup.ShortcutKeys = Keys.Control | Keys.Oemplus;
            popup.ShortcutKeyDisplayString = "Ctrl+Plus";
            popup.Click += popup_handler;
            ui.Menus.mnuPlugins.DropDownItems.Add(popup);
            selector = new Selector(CamBamUI.MainUI);

            ThisApplication.TopWindow.Load += on_load;
        }
    }
}
