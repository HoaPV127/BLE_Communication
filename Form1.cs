using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using Windows.Devices;
using Windows.Foundation;
using Windows;
using Windows.Storage.Streams;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Threading;
using System.Windows.Threading;

namespace BLE_Communication
{
    public partial class Form1 : Form
    {

        //private BluetoothLEAdvertisementPublisher m_publisher;
        private BluetoothLEAdvertisementWatcher m_watcher;
        private Dictionary<ulong, string> m_devices;
        //private List<ulong> m_devices;
        private BluetoothLEDevice currDevice;
        private Dispatcher dispatcher;
        private static Mutex mutex;
        public Form1()
        {

            InitializeComponent();

            dispatcher = Dispatcher.CurrentDispatcher;
            mutex = new Mutex();

            m_devices = new Dictionary<ulong, string>();
            //m_devices = new List<ulong>();
            m_watcher = new BluetoothLEAdvertisementWatcher();

            Console.WriteLine("Start Scan");
            // find the CompanyID to filter, comment out to scan all
            //BluetoothLEManufacturerData manuInfo = new BluetoothLEManufacturerData();
            //manuInfo.CompanyId = 19784;     // for HM-10
            //manuInfo.CompanyId = 35289;     // for BT-05
            //m_watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manuInfo);

            m_watcher.ScanningMode = BluetoothLEScanningMode.Passive;
            m_watcher.Received += OnAdvertisementReceived;
            
            m_watcher.Start();

        }

        private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            bool deviceExist = false;

            mutex.WaitOne();
            deviceExist = m_devices.ContainsKey(eventArgs.BluetoothAddress);
            mutex.ReleaseMutex();

            if (deviceExist == false)
            {
                AddDeviceAsync(eventArgs.BluetoothAddress);
            }
        }

        private async void AddDeviceAsync(ulong addr)
        {
            string devName = null;

            BluetoothLEDevice scanDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);

            await dispatcher.BeginInvoke((Action)delegate
            {
                if (scanDevice != null)
                    devName = scanDevice.Name;
                else
                    return;
            });

            //if (scanDevice == null) return;

            Button dev = new Button();

            mutex.WaitOne();
            m_devices.Add(addr, devName);
            dev.Location = new System.Drawing.Point(this.Width - 190, 30 * m_devices.Count);
            mutex.ReleaseMutex();

            if (string.IsNullOrEmpty(devName))
                dev.Text = "Unknown";
            else
                dev.Text = devName;
            dev.Width = 150;
            dev.Height = 30;
            dev.Click += (sender, EventArgs) => { SelectAndConnect(addr); };

            if (this.InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    this.Controls.Add(dev);
                }));
            }
            else
            {
                this.Controls.Add(dev);
            }

        }


        private async void SelectAndConnect(ulong addr)
        {
            currDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
            BluetoothLEDevice device = currDevice;

            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                return;
            }

            // pair device
            DeviceAccessStatus status = await device.RequestAccessAsync();

            if (status == DeviceAccessStatus.Allowed)
            {
                if (this.msgRTbx.InvokeRequired)
                {
                    Invoke(new Action(() =>
                    {
                        this.msgRTbx.AppendText($"Connected to {device.Name}\n");
                        msgRTbx.ScrollToCaret();
                    }));
                }
                else
                {
                    this.msgRTbx.AppendText($"Connected to {device.Name}\n");
                    msgRTbx.ScrollToCaret();
                }
            }

            GattDeviceServicesResult devService = await device.GetGattServicesAsync();

            if (devService.Status == GattCommunicationStatus.Success)
            {
                var services = devService.Services;

                foreach (var service in services)
                {
                    if (!service.Uuid.ToString().Contains("0000ffe0-"))
                    {
                        continue;
                    }
                    GattCharacteristicsResult devCharacterist = await service.GetCharacteristicsAsync();

                    if (devCharacterist.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in devCharacterist.Characteristics)
                        {
                            GattCharacteristicProperties devProperties = characteristic.CharacteristicProperties;

                            if (devProperties.HasFlag(GattCharacteristicProperties.Notify))
                            {
                                GattCommunicationStatus commStatus = GattCommunicationStatus.AccessDenied;

                                try
                                {
                                    commStatus = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                }
                                catch (Exception ex)
                                {
                                    if (this.msgRTbx.InvokeRequired)
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            this.msgRTbx.AppendText(ex.Message);
                                            msgRTbx.ScrollToCaret();
                                        }));
                                    }
                                    else
                                    {
                                        this.msgRTbx.AppendText(ex.Message);
                                        msgRTbx.ScrollToCaret();
                                    }
                                }
                                if (commStatus == GattCommunicationStatus.Success)
                                {
                                    characteristic.ValueChanged += Characteristic_ValueChanged;
                                    // Server has been informed of clients interest.
                                }
                            }
                        }
                    }
                }
            }
        }

        void Characteristic_ValueChanged(GattCharacteristic eventSender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);

            var value = reader.ReadString(args.CharacteristicValue.Length);

            if (this.msgRTbx.InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    this.msgRTbx.AppendText($"{currDevice.Name}: {value}\n");
                    msgRTbx.ScrollToCaret();
                }));
            }
            else
            {
                this.msgRTbx.AppendText($"{currDevice.Name}: {value}\n");
                msgRTbx.ScrollToCaret();
            }
        }

        private async void sendBtn_Click(object sender, EventArgs e)
        {
            int retry = 3;

            string sendText = this.sendTbx.Text.ToString();

            var writer = new DataWriter();
            // WriteByte used for simplicity. Other common functions - WriteInt16 and WriteSingle
            writer.WriteString(sendText);
            GattCommunicationStatus result = GattCommunicationStatus.Unreachable;

            GattDeviceServicesResult devService = null;

            while (--retry > 0)
            {
                devService = await currDevice.GetGattServicesAsync();

                if (devService != null && devService.Status == GattCommunicationStatus.Success)
                    break;
            }

            if (devService != null && devService.Status == GattCommunicationStatus.Success)
            { 
                var services = devService.Services;

                foreach (var service in services)
                {
                    if (!service.Uuid.ToString().Contains("0000ffe0-"))
                    {
                        continue;
                    }

                    GattCharacteristicsResult devCharacterist = null;

                    while (--retry > 0)
                    {
                        devCharacterist = await service.GetCharacteristicsAsync();

                        if (devCharacterist != null && devCharacterist.Status == GattCommunicationStatus.Success)
                            break;
                    }

                    if (devCharacterist != null && devCharacterist.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in devCharacterist.Characteristics)
                        {
                            GattCharacteristicProperties devProperties = characteristic.CharacteristicProperties;

                            if (devProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                            {
                                try
                                {
                                    result = await characteristic.WriteValueAsync(writer.DetachBuffer());
                                }
                                catch (Exception ex)
                                {
                                    if (this.msgRTbx.InvokeRequired)
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            this.msgRTbx.AppendText($"{ex.Message}\n");
                                            msgRTbx.ScrollToCaret();
                                        }));
                                    }
                                    else
                                    {
                                        this.msgRTbx.AppendText($"{ex.Message}\n");
                                        msgRTbx.ScrollToCaret();
                                    }
                                }

                                if (result == GattCommunicationStatus.Success)
                                {
                                    if (this.msgRTbx.InvokeRequired)
                                    {
                                        Invoke(new Action(() =>
                                        {
                                            this.msgRTbx.AppendText($"Send to {currDevice.Name}: {sendText}\n");
                                            msgRTbx.ScrollToCaret();
                                        }));
                                    }
                                    else
                                    {
                                        this.msgRTbx.AppendText($"Send to {currDevice.Name}: {sendText}\n");
                                        msgRTbx.ScrollToCaret();
                                    }
                                }

                            }
                        }
                    }
                }
            }
        }

    }
}
