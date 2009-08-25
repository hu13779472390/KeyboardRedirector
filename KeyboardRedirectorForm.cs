using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace KeyboardRedirector
{
    public partial class KeyboardRedirectorForm : MinimizeToTrayForm
    {
        InputDevice _inputDevice;
        List<DeviceInformation> _keyboards;
        List<KeyToHookInformation> _keysToHook;

        class DeviceInformation
        {
            public InputDevice.DeviceInfo DeviceInfo = null;

            public DeviceInformation(InputDevice.DeviceInfo DeviceInfo)
            {
                this.DeviceInfo = DeviceInfo;
            }

            public override string ToString()
            {
                return DeviceInfo.Name;
            }
        }

        class KeyToHookInformation
        {
            public ushort VirtualKeyCode;
            public DateTime SeenAt;

            public KeyToHookInformation(ushort VirtualKeyCode)
            {
                this.VirtualKeyCode = VirtualKeyCode;
                this.SeenAt = DateTime.Now;
            }
        }

        public KeyboardRedirectorForm()
        {
            _keyboards = new List<DeviceInformation>();
            _keysToHook = new List<KeyToHookInformation>();

            InitializeComponent();
            treeViewKeys.Nodes.Clear();
            panelKeyboardProperties.Location = new Point(3, 3);
            panelKeyProperties.Location = new Point(3, 3);

            _inputDevice = new InputDevice(Handle);
            _inputDevice.DeviceEvent += new InputDevice.DeviceEventHandler(InputDevice_DeviceEvent);

            RefreshDevices();

            NotifyIcon.ContextMenuStrip = contextMenuStripNotifyIcon;

            if (Settings.Current.MinimizeOnStart)
            {
                SendToTray();
                this.ShowInTaskbar = false;
            }

            bool result = KeyboardHook.SetHook(this);
            if (result == false)
                richTextBoxEvents.AppendText("Failed to set hook" + Environment.NewLine);
        }

        private void KeyboardRedirectorForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            KeyboardHook.ClearHook();
        }

        private void RefreshDevices()
        {
            List<string> keyboardsBeforeRefresh = new List<string>();
            foreach (DeviceInformation deviceInformation in _keyboards)
            {
                keyboardsBeforeRefresh.Add(deviceInformation.DeviceInfo.DeviceName);
            }

            _inputDevice.EnumerateDevices();
            foreach (InputDevice.DeviceInfo info in _inputDevice.DeviceList.Values)
            {
                if (info.DeviceType != InputDevice.DeviceType.Keyboard)
                    continue;
                if (info.DeviceHandle == IntPtr.Zero)
                    continue;

                if (keyboardsBeforeRefresh.Contains(info.DeviceName) == false)
                {
                    _keyboards.Add(new DeviceInformation(info));

                    SettingsKeyboard keyboard = Settings.Current.FindKeyboardByDeviceName(info.DeviceName);
                    if (keyboard == null)
                    {
                        keyboard = new SettingsKeyboard();
                        keyboard.Name = info.Name;
                        //keyboard.Handle = (uint)info.DeviceHandle.ToInt32();
                        keyboard.DeviceName = info.DeviceName;
                        Settings.Current.Keyboards.Add(keyboard);
                        Settings.Save();
                        richTextBoxEvents.AppendText("New Keyboard Added : " + keyboard.Name + Environment.NewLine);
                    }
                    else
                    {
                        richTextBoxEvents.AppendText("Keyboard Added : " + keyboard.Name + Environment.NewLine);
                    }
                }
                else
                {
                    keyboardsBeforeRefresh.Remove(info.DeviceName);
                }
            }

            foreach (string deviceName in keyboardsBeforeRefresh)
            {
                DeviceInformation deviceInformation = FindKeyboardDevice(deviceName);
                _keyboards.Remove(deviceInformation);

                SettingsKeyboard keyboard = Settings.Current.FindKeyboardByDeviceName(deviceName);
                richTextBoxEvents.AppendText("Keyboard Removed : " + keyboard.Name + Environment.NewLine);
            }

            RefreshTreeView();
        }

        protected override void WndProc(ref Message message)
        {
            if (_inputDevice != null)
            {
                //Message msg;
                //NativeMethods.PeekMessage(out msg, _handle, NativeMethods.WM_INPUT, NativeMethods.WM_INPUT, NativeMethods.PeekMessageRemoveFlag.PM_NOREMOVE);
                //if (msg.Msg != 0)
                //{
                //    Debug.WriteLine("PeekMessage found message waiting.");
                //    id.ProcessMessage(msg);
                //}

                _inputDevice.ProcessMessage(message);
            }

            if (message.Msg == KeyboardHook.HookMessage)
            {
                uint wParam = (uint)message.WParam.ToInt32();
                bool peekMessage = ((wParam >> 31) != 0);
                wParam = wParam & 0x7FFFFFFF;

                KeyboardHook.KeyboardParams parameters = KeyboardHook.ConvertLParamToKeyboardParams(message.LParam.ToInt32());
                //System.Diagnostics.Debug.WriteLine(
                //    "HookMsg: " + (peekMessage ? "Peek" : "    ") +
                //    " wparam=0x" + wParam.ToString("x") + " (" + ((Keys)wParam).ToString() + ")" +
                //    " repeatCount=0x" + parameters.repeatCount.ToString("x") +
                //    " scanCode=0x" + parameters.scanCode.ToString("x") +
                //    " transitionState=" + parameters.transitionState.ToString() +
                //    " extendedKey=" + parameters.extendedKey.ToString() +
                //    " contextCode=" + parameters.contextCode.ToString() +
                //    " previousKeyState=" + parameters.previousKeyState.ToString()
                //    );

                bool block = false;
                lock (_keysToHook)
                {
                    for (int i = 0; i < _keysToHook.Count; i++)
                    {
                        if (_keysToHook[i].VirtualKeyCode == wParam)
                        {
                            block = true;
                            _keysToHook.RemoveAt(i);
                        }
                    }

                    // Remove any old stale entries
                    while ((_keysToHook.Count > 0) && (DateTime.Now.Subtract(_keysToHook[0].SeenAt).TotalMilliseconds > 500))
                    {
                        _keysToHook.RemoveAt(0);
                    }
                }

                if (block)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Blocking: " + (peekMessage ? "Peek" : "    ") +
                        " wparam=0x" + wParam.ToString("x") + " (" + ((Keys)wParam).ToString() + ")" +
                        " repeatCount=0x" + parameters.repeatCount.ToString("x") +
                        " scanCode=0x" + parameters.scanCode.ToString("x") +
                        " transitionState=" + parameters.transitionState.ToString() +
                        " extendedKey=" + parameters.extendedKey.ToString() +
                        " contextCode=" + parameters.contextCode.ToString() +
                        " previousKeyState=" + parameters.previousKeyState.ToString()
                        );

                    message.Result = new IntPtr(-1);
                    return;
                }

                return;
            }

            if (message.Msg == (int)Win32.WindowsMessage.DEVICECHANGE)
            {
                //System.Diagnostics.Debug.WriteLine(message.ToString());
                RefreshDevices();
            }
            
            base.WndProc(ref message);
        }

        void InputDevice_DeviceEvent(object sender, InputDevice.DeviceInfo dInfo, InputDevice.RAWINPUT rawInput)
        {
            if (rawInput.header.dwType == InputDevice.DeviceType.Keyboard)
            {
                {
                    Keys key = (Keys)rawInput.data.keyboard.VKey;
                    string text = string.Format("{0} 0x{1:x}({2}) 0x{3:x} {4}",
                        rawInput.header.dwType,
                        rawInput.data.keyboard.VKey,
                        key,
                        rawInput.data.keyboard.MakeCode,
                        rawInput.data.keyboard.Message);

                    System.Diagnostics.Debug.WriteLine("WM_INPUT: 0x" + dInfo.DeviceHandle.ToInt32().ToString("x8") + " " + text);
                }

                if (richTextBoxKeyDetector.Focused)
                {
                    lock (_keysToHook)
                    {
                        _keysToHook.Add(new KeyToHookInformation(rawInput.data.keyboard.VKey));
                    }

                    AddAndSelectKey(dInfo.DeviceName, rawInput.data.keyboard.VKey);

                    return;
                }

                lock (Settings.Current)
                {
                    SettingsKeyboard keyboard = Settings.Current.FindKeyboardByDeviceName(dInfo.DeviceName);
                    if (keyboard != null)
                    {
                        if (keyboard.CaptureAllKeys)
                        {
                            lock (_keysToHook)
                            {
                                _keysToHook.Add(new KeyToHookInformation(rawInput.data.keyboard.VKey));
                            }
                        }
                        else
                        {
                            SettingsKeyboardKey key = keyboard.FindKey(rawInput.data.keyboard.VKey);
                            if (key != null)
                            {
                                if (key.Capture)
                                {
                                    lock (_keysToHook)
                                    {
                                        _keysToHook.Add(new KeyToHookInformation(rawInput.data.keyboard.VKey));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private DeviceInformation FindKeyboardDevice(string deviceName)
        {
            foreach (DeviceInformation info in _keyboards)
            {
                if (info.DeviceInfo.DeviceName == deviceName)
                    return info;
            }
            return null;
        }




        private void restoreToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RestoreFromTray();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }


        private void RefreshTreeView()
        {
            lock (treeViewKeys)
            {
                treeViewKeys.BeginUpdate();

                // Update the keyboard nodes

                List<TreeNode> staleKeyboardNodes = new List<TreeNode>();
                foreach (TreeNode node in treeViewKeys.Nodes)
                {
                    staleKeyboardNodes.Add(node);
                }

                foreach (SettingsKeyboard keyboard in Settings.Current.Keyboards)
                {
                    // Find existing node and remove it from the stale list
                    TreeNode keyboardNode = FindTreeNode(keyboard, treeViewKeys.Nodes, false);
                    if (keyboardNode != null)
                    {
                        staleKeyboardNodes.Remove(keyboardNode);
                    }
                    else
                    {
                        // No keyboard node. We'll create one.
                        keyboardNode = new TreeNode(keyboard.Name);
                        keyboardNode.Tag = keyboard;

                        treeViewKeys.Nodes.Add(keyboardNode);
                    }

                    // Update node data
                    keyboardNode.Text = keyboard.Name;

                    DeviceInformation deviceInformation = FindKeyboardDevice(keyboard.DeviceName);
                    int imageIndex = 0;
                    if (deviceInformation == null)
                        imageIndex = 1;
                    else if (keyboard.CaptureAllKeys)
                        imageIndex = 3;
                    keyboardNode.ImageIndex = imageIndex;
                    keyboardNode.SelectedImageIndex = imageIndex;


                    // Update keys
                    List<TreeNode> staleKeyNodes = new List<TreeNode>();
                    foreach (TreeNode node in keyboardNode.Nodes)
                    {
                        staleKeyNodes.Add(node);
                    }

                    foreach (SettingsKeyboardKey key in keyboard.Keys)
                    {
                        // Find existing node and remove it from the stale list
                        TreeNode keyNode = FindTreeNode(key, keyboardNode.Nodes, false);
                        if (keyNode != null)
                        {
                            staleKeyNodes.Remove(keyNode);
                        }
                        else
                        {
                            // No key node. We'll create one.
                            keyNode = new TreeNode("0x" + key.VirtualKeyCode.ToString("x2") + " - " + key.Name);
                            keyNode.Tag = key;

                            bool expand = (keyboardNode.Nodes.Count == 0);
                            keyboardNode.Nodes.Add(keyNode);
                            if (expand)
                                keyboardNode.Expand();
                        }

                        // Update node data
                        keyNode.Text = "0x" + key.VirtualKeyCode.ToString("x2") + " - " + key.Name;
                        imageIndex = 2;
                        if (key.Capture)
                            imageIndex = 3;
                        else if (keyboard.CaptureAllKeys)
                            imageIndex = 4;

                        keyNode.ImageIndex = imageIndex;
                        keyNode.SelectedImageIndex = imageIndex;
                    }

                    // Remove stale key nodes.
                    foreach (TreeNode node in staleKeyNodes)
                    {
                        keyboardNode.Nodes.Remove(node);
                    }

                }

                // Remove stale keyboard nodes.
                foreach (TreeNode node in staleKeyboardNodes)
                {
                    treeViewKeys.Nodes.Remove(node);
                }


                treeViewKeys.EndUpdate();
            }
        }

        private TreeNode FindTreeNode(object tag, TreeNodeCollection baseCollection, bool includeChildren)
        {
            lock (treeViewKeys)
            {
                List<TreeNodeCollection> collections = new List<TreeNodeCollection>();
                collections.Add(baseCollection);
                while (collections.Count > 0)
                {
                    foreach (TreeNode node in collections[0])
                    {
                        if (node.Tag.Equals(tag))
                            return node;

                        if (includeChildren && (node.Nodes.Count > 0))
                            collections.Add(node.Nodes);
                    }
                    collections.RemoveAt(0);
                }
            }
            return null;
        }

        private void AddAndSelectKey(string deviceName, ushort VirtualKeyCode)
        {
            SettingsKeyboard keyboard = Settings.Current.FindKeyboardByDeviceName(deviceName);
            if (keyboard == null)
                return;

            SettingsKeyboardKey key = keyboard.FindKey(VirtualKeyCode);
            if (key != null)
            {
                lock (treeViewKeys)
                {
                    TreeNode node = FindTreeNode(key, treeViewKeys.Nodes, true);
                    if (node != null)
                        treeViewKeys.SelectedNode = node;
                }
            }
            else
            {
                key = new SettingsKeyboardKey(VirtualKeyCode);
                keyboard.Keys.Add(key);
                Settings.Save();

                lock (treeViewKeys)
                {
                    RefreshTreeView();

                    TreeNode node = FindTreeNode(key, treeViewKeys.Nodes, true);
                    if (node != null)
                        treeViewKeys.SelectedNode = node;
                }
            }

        }


        private void treeViewKeys_AfterSelect(object sender, TreeViewEventArgs e)
        {
            SettingsKeyboard keyboard = e.Node.Tag as SettingsKeyboard;
            SettingsKeyboardKey key = e.Node.Tag as SettingsKeyboardKey;

            panelKeyboardProperties.Visible = false;
            panelKeyProperties.Visible = false;

            if (keyboard != null)
            {
                panelKeyboardProperties.Visible = true;

                StringBuilder details = new StringBuilder();
                details.AppendLine("DeviceName: " + keyboard.DeviceName);

                DeviceInformation deviceInformation = FindKeyboardDevice(keyboard.DeviceName);
                if ((deviceInformation == null) || (deviceInformation.DeviceInfo == null))
                {
                    details.Append("Device not present.");
                }
                else
                {
                    details.Append("DeviceDesc: " + deviceInformation.DeviceInfo.DeviceDesc);
                }

                textBoxKeyboardDetails.Text = details.ToString();
                textBoxKeyboardName.Text = keyboard.Name;

                checkBoxCaptureAllKeys.Checked = keyboard.CaptureAllKeys;
            }
            else if (key != null)
            {
                panelKeyProperties.Visible = true;

                StringBuilder details = new StringBuilder();
                details.AppendLine("Virtual Key Code: 0x" + key.VirtualKeyCode.ToString("x2"));

                labelKeyDetails.Text = details.ToString();
                textBoxKeyName.Text = key.Name;

                checkBoxCaptureKey.Checked = key.Capture;
            }
        }

        private void checkBoxCaptureAllKeys_CheckedChanged(object sender, EventArgs e)
        {
            TreeNode node = treeViewKeys.SelectedNode;
            if (node == null)
                return;

            SettingsKeyboard keyboard = node.Tag as SettingsKeyboard;
            if (keyboard != null)
            {
                keyboard.CaptureAllKeys = checkBoxCaptureAllKeys.Checked;
                Settings.Save();
                RefreshTreeView();
            }

        }

        private void checkBoxCaptureKey_CheckedChanged(object sender, EventArgs e)
        {
            TreeNode node = treeViewKeys.SelectedNode;
            if (node == null)
                return;

            SettingsKeyboardKey key = node.Tag as SettingsKeyboardKey;
            if (key != null)
            {
                key.Capture = checkBoxCaptureKey.Checked;
                Settings.Save();
                RefreshTreeView();
            }

        }

        private void textBoxKeyboardName_TextChanged(object sender, EventArgs e)
        {
            if (textBoxKeyboardName.Text.Length == 0)
                return;

            TreeNode node = treeViewKeys.SelectedNode;
            if (node == null)
                return;

            SettingsKeyboard keyboard = node.Tag as SettingsKeyboard;
            if (keyboard != null)
            {
                keyboard.Name = textBoxKeyboardName.Text;
                Settings.Save();
                RefreshTreeView();
            }

        }

        private void textBoxKeyName_TextChanged(object sender, EventArgs e)
        {
            if (textBoxKeyName.Text.Length == 0)
                return;

            TreeNode node = treeViewKeys.SelectedNode;
            if (node == null)
                return;

            SettingsKeyboardKey key = node.Tag as SettingsKeyboardKey;
            if (key != null)
            {
                key.Name = textBoxKeyName.Text;
                Settings.Save();
                RefreshTreeView();
            }

        }


        private void treeViewKeys_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Modifiers == Keys.None)
            {
                if (e.KeyCode == Keys.Delete)
                {
                    TreeNode node = treeViewKeys.SelectedNode;
                    if (node == null)
                        return;

                    SettingsKeyboard keyboard = node.Tag as SettingsKeyboard;
                    SettingsKeyboardKey key = node.Tag as SettingsKeyboardKey;
                    if (keyboard != null)
                    {
                        DialogResult result = MessageBox.Show(this, "Are you sure you want to delete this keyboard?" + Environment.NewLine + keyboard.Name, "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                        if (result != DialogResult.Yes)
                            return;
                        Settings.Current.Keyboards.Remove(keyboard);
                        Settings.Save();
                        RefreshTreeView();
                    }
                    else if (key != null)
                    {
                        DialogResult result = MessageBox.Show(this, "Are you sure you want to delete this key?" + Environment.NewLine + key.Name, "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                        if (result != DialogResult.Yes)
                            return;
                        keyboard = node.Parent.Tag as SettingsKeyboard;
                        keyboard.Keys.Remove(key);
                        Settings.Save();
                        RefreshTreeView();
                    }

                }
            }
        }

    }
}