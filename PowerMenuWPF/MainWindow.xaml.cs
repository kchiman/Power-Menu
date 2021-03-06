﻿using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;

namespace PowerMenuWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern void LockWorkStation();

        [DllImport("powrprof.dll")]
        public static extern bool IsPwrHibernateAllowed();

        [DllImport("powrprof.dll")]
        public static extern bool IsPwrSuspendAllowed();

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TokPriv1Luid
        {
            public int Count;
            public long Luid;
            public int Attr;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

        internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const int TOKEN_QUERY = 0x00000008;
        internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
        internal const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

        ManagementBaseObject mboShutdown;
        ManagementClass mcWin32;

        public MainWindow()
        {
            InitializeComponent();
            gatherAllOSInfo();
            setStatusBar();
            initWaitTimeList();
            initForm();
        }

        protected void initWMI()
        {
            if (mcWin32 == null)
                mcWin32 = new ManagementClass("Win32_OperatingSystem");
            mcWin32.Get();
        }

        protected void initWaitTimeList()
        {
            if (Utils.IsWinVistaOrHigher())
            // if (Utils.isNT6OrAbove(getOSInfo(OSInfo.Version)))
            {
                cbxWaitTime.IsEnabled = true;
                cbxWaitTime.SelectedIndex = (int)Utils.getSettingValue("waitTime");
            }
            else
            {
                cbxWaitTime.IsEnabled = false;
            }
        }

        protected void initForm()
        {
            cbxShowConfirmMsg.IsChecked = (bool)Utils.getSettingValue("showConfirm");
            string lang = (string)Utils.getSettingValue("lang");
            
            if (lang.ToLower().Equals("zh-hk"))
            {
                tmiZH.IsChecked = true;
                tmiEng.IsChecked = false;
            }
            else
            {
                tmiZH.IsChecked = false;
                tmiEng.IsChecked = true;
            }

            if (IsPwrHibernateAllowed())
                btnHibernate.IsEnabled = true;
            else
                btnHibernate.IsEnabled = false;

            if (IsPwrSuspendAllowed())
                btnSleep.IsEnabled = true;
            else
                btnSleep.IsEnabled = false;
        }

        protected void setStatusBar()
        {
            tlblWindowsVersion.Text = String.Format("{0}", getOSInfo(OSInfo.Caption));
        }

        protected void gatherAllOSInfo()
        {
            Utils.resetOSInfoHashTable();
            initWMI();
            foreach (ManagementObject manObj in mcWin32.GetInstances())
            {
                // See: http://msdn.microsoft.com/en-us/library/windows/desktop/aa394239(v=vs.85).aspx
                Utils.addToOSInfoHashTable("Version", manObj["Version"].ToString());
                Utils.addToOSInfoHashTable("Caption", manObj["Caption"].ToString());
            }
        }

        protected string getOSInfo(string property)
        {
            Utils.initOSInfoHashTable();

            if (Utils.getOSInfoHashTableCount() == 0)
                gatherAllOSInfo();

            if (Utils.isOSInfoContainsKey(property))
                return Utils.getOSInfoByKey(property);
            return "NULL";
        }

        protected void powerManagement(PowerOptions option)
        {
            bool ok;
            TokPriv1Luid tp;
            IntPtr hproc = GetCurrentProcess();
            IntPtr htok = IntPtr.Zero;
            ok = OpenProcessToken(hproc, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref htok);
            tp.Count = 1;
            tp.Luid = 0;
            tp.Attr = SE_PRIVILEGE_ENABLED;
            ok = LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, ref tp.Luid);
            ok = AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);

            if (mcWin32 == null)
                initWMI();

            mcWin32.Scope.Options.EnablePrivileges = true;
            string classPath = String.Empty;
            ManagementBaseObject mboShutdownParams = null;

            if (Utils.IsWinVistaOrHigher())
            // if (Utils.isNT6OrAbove(getOSInfo(OSInfo.Version)))
            {
                // See: http://msdn.microsoft.com/en-us/library/windows/desktop/aa394057(v=vs.85).aspx
                classPath = ShutdownClassPath.NT6;
                string comment = String.Empty;
                mboShutdownParams = mcWin32.GetMethodParameters(classPath);
                mboShutdownParams["Flags"] = ((int)option).ToString();
                mboShutdownParams["ReasonCode"] = "0";
                if (option == PowerOptions.REBOOT)
                    comment = Properties.Resources.strRebootComment;
                else if (option == PowerOptions.SHUTDOWN)
                    comment = Properties.Resources.strShutdownComment;
                mboShutdownParams["Comment"] = comment;
                mboShutdownParams["Timeout"] = cbxWaitTime.Text;
            }
            else
            {
                // See: http://msdn.microsoft.com/en-us/library/windows/desktop/aa394058(v=vs.85).aspx
                classPath = ShutdownClassPath.NT5;
                mboShutdownParams = mcWin32.GetMethodParameters(classPath);
                mboShutdownParams["Flags"] = ((int)option).ToString();
                mboShutdownParams["Reserved"] = "0";
            }

            foreach (ManagementObject manObj in mcWin32.GetInstances())
            {
                mboShutdown = manObj.InvokeMethod(classPath, mboShutdownParams, null);
            }
        }

        private void btnShutDown_Click(object sender, RoutedEventArgs e)
        {
            if (cbxShowConfirmMsg.IsChecked == true)
            {
                if (Utils.getConfirmMsgResult(PowerOptions.SHUTDOWN) == MessageBoxResult.Yes)
                {
                    powerManagement(PowerOptions.SHUTDOWN);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                powerManagement(PowerOptions.SHUTDOWN);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void btnRestart_Click(object sender, RoutedEventArgs e)
        {
            if (cbxShowConfirmMsg.IsChecked == true)
            {
                if (Utils.getConfirmMsgResult(PowerOptions.REBOOT) == MessageBoxResult.Yes)
                {
                    powerManagement(PowerOptions.REBOOT);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                powerManagement(PowerOptions.REBOOT);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void btnLogOff_Click(object sender, RoutedEventArgs e)
        {
            if (cbxShowConfirmMsg.IsChecked == true)
            {
                if (Utils.getConfirmMsgResult(PowerOptions.LOGOFF) == MessageBoxResult.Yes)
                {
                    powerManagement(PowerOptions.LOGOFF);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                powerManagement(PowerOptions.LOGOFF);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void btnHibernate_Click(object sender, RoutedEventArgs e)
        {
            if (cbxShowConfirmMsg.IsChecked == true)
            {
                if (Utils.getConfirmMsgResult(PowerOptions.HIBERNATE) == MessageBoxResult.Yes)
                {
                    System.Windows.Forms.Application.SetSuspendState(PowerState.Hibernate, true, false);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                System.Windows.Forms.Application.SetSuspendState(PowerState.Hibernate, true, false);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void btnSleep_Click(object sender, RoutedEventArgs e)
        {
            if (cbxShowConfirmMsg.IsChecked == true)
            {
                if (Utils.getConfirmMsgResult(PowerOptions.SLEEP) == MessageBoxResult.Yes)
                {
                    System.Windows.Forms.Application.SetSuspendState(PowerState.Suspend, true, false);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                System.Windows.Forms.Application.SetSuspendState(PowerState.Suspend, true, false);
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void btnLock_Click(object sender, RoutedEventArgs e)
        {
            if (cbxShowConfirmMsg.IsChecked == true)
            {
                if (Utils.getConfirmMsgResult(PowerOptions.LOCK) == MessageBoxResult.Yes)
                {
                    LockWorkStation();
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                LockWorkStation();
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void SwitchLanguage_Click(object sender, RoutedEventArgs e)
        {
            if (sender.GetType().Equals(typeof(System.Windows.Controls.MenuItem)))
            {
                string lang = ((System.Windows.Controls.MenuItem)sender).Tag.ToString().Replace('_', '-');
                Utils.updateSetting("lang", lang);

                System.Windows.Forms.Application.Restart();
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            string[] keys = { "showConfirm", "waitTime" };
            object[] values = { cbxShowConfirmMsg.IsChecked, cbxWaitTime.SelectedIndex };
            Utils.saveAllSetting(keys, values);
        }

        private void tmiExit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void tlblDeveloper_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Process.Start("https://www.facebook.com/cmproducts.apps");
        }
    }
}
