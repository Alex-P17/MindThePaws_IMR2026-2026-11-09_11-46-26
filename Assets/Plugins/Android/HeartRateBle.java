package com.mindthepaws.ble;

import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.util.Log;

import java.util.Collections;
import java.util.UUID;

/**
 * Citește pulsul de la orice centură / brățară BLE standard (Heart Rate Service 0x180D),
 * de ex. senzorii Decathlon (Kalenji / Domyos). Apelat din Unity prin HeartRateMonitor.cs.
 */
public final class HeartRateBle {
    private static final String TAG = "MindThePawsHR";
    private static final UUID HR_SERVICE = UUID.fromString("0000180d-0000-1000-8000-00805f9b34fb");
    private static final UUID HR_MEASUREMENT = UUID.fromString("00002a37-0000-1000-8000-00805f9b34fb");
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private static Context ctx;
    private static BluetoothLeScanner scanner;
    private static BluetoothGatt gatt;
    private static volatile int bpm = -1;
    private static volatile long lastMs = 0;
    private static volatile String status = "oprit";
    private static volatile String deviceName = "";
    private static volatile boolean running = false;
    private static boolean scanning = false;
    private static final Handler main = new Handler(Looper.getMainLooper());

    private HeartRateBle() {}

    public static void start(final Context context) {
        main.post(new Runnable() { public void run() { startInternal(context); } });
    }

    public static void stop() {
        main.post(new Runnable() { public void run() {
            running = false;
            stopScan();
            if (gatt != null) { try { gatt.close(); } catch (Exception ignored) {} gatt = null; }
            status = "oprit";
        } });
    }

    /** BPM-ul curent sau -1 dacă nu avem o valoare din ultimele 5 secunde. */
    public static int getBpm() {
        if (bpm <= 0 || System.currentTimeMillis() - lastMs > 5000) return -1;
        return bpm;
    }

    public static String getStatus() { return status; }
    public static String getDeviceName() { return deviceName; }

    private static void startInternal(Context context) {
        if (running) return;
        ctx = context.getApplicationContext();
        running = true;
        try {
            BluetoothManager bm = (BluetoothManager) ctx.getSystemService(Context.BLUETOOTH_SERVICE);
            BluetoothAdapter adapter = bm != null ? bm.getAdapter() : null;
            if (adapter == null) { status = "fără Bluetooth"; running = false; return; }
            if (!adapter.isEnabled()) { status = "Bluetooth oprit în setările căștii"; running = false; return; }
            scanner = adapter.getBluetoothLeScanner();
            startScan();
        } catch (SecurityException e) {
            status = "lipsește permisiunea Bluetooth"; running = false;
        } catch (Exception e) {
            status = "eroare: " + e.getMessage(); running = false;
        }
    }

    private static void startScan() {
        if (scanner == null || scanning || !running) return;
        try {
            ScanFilter filter = new ScanFilter.Builder().setServiceUuid(new ParcelUuid(HR_SERVICE)).build();
            ScanSettings settings = new ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build();
            scanner.startScan(Collections.singletonList(filter), settings, scanCb);
            scanning = true;
            status = "caut senzorul...";
        } catch (SecurityException e) {
            status = "lipsește permisiunea de scanare";
        }
    }

    private static void stopScan() {
        if (scanner == null || !scanning) return;
        try { scanner.stopScan(scanCb); } catch (Exception ignored) {}
        scanning = false;
    }

    private static final ScanCallback scanCb = new ScanCallback() {
        @Override public void onScanResult(int callbackType, ScanResult result) {
            if (gatt != null || !running) return;
            stopScan();
            BluetoothDevice d = result.getDevice();
            try {
                deviceName = d.getName() != null ? d.getName() : d.getAddress();
                status = "mă conectez la " + deviceName + "...";
                gatt = d.connectGatt(ctx, false, gattCb, BluetoothDevice.TRANSPORT_LE);
            } catch (SecurityException e) {
                status = "lipsește permisiunea de conectare";
            }
        }
        @Override public void onScanFailed(int errorCode) {
            scanning = false;
            status = "scanare eșuată (" + errorCode + ")";
            retryLater();
        }
    };

    private static void retryLater() {
        main.postDelayed(new Runnable() { public void run() { if (running && gatt == null) startScan(); } }, 3000);
    }

    private static final BluetoothGattCallback gattCb = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(BluetoothGatt g, int st, int newState) {
            try {
                if (newState == BluetoothProfile.STATE_CONNECTED) {
                    status = "conectat, citesc serviciile...";
                    g.discoverServices();
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    status = "deconectat, reîncerc...";
                    try { g.close(); } catch (Exception ignored) {}
                    main.post(new Runnable() { public void run() { gatt = null; retryLater(); } });
                }
            } catch (SecurityException e) { status = "lipsește permisiunea de conectare"; }
        }

        @Override public void onServicesDiscovered(BluetoothGatt g, int st) {
            try {
                BluetoothGattService s = g.getService(HR_SERVICE);
                BluetoothGattCharacteristic c = s != null ? s.getCharacteristic(HR_MEASUREMENT) : null;
                if (c == null) { status = "senzorul nu are serviciu de puls"; return; }
                g.setCharacteristicNotification(c, true);
                BluetoothGattDescriptor d = c.getDescriptor(CCCD);
                if (d != null) {
                    d.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                    g.writeDescriptor(d);
                }
                status = "conectat: " + deviceName;
            } catch (SecurityException e) { status = "lipsește permisiunea de conectare"; }
        }

        // Android < 13
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
            if (HR_MEASUREMENT.equals(c.getUuid())) parse(c.getValue());
        }

        // Android 13+
        @Override public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c, byte[] value) {
            if (HR_MEASUREMENT.equals(c.getUuid())) parse(value);
        }
    };

    /** Formatul standard Heart Rate Measurement: bitul 0 din flags = valoare pe 16 biți. */
    private static void parse(byte[] v) {
        if (v == null || v.length < 2) return;
        int flags = v[0] & 0xFF;
        int value = (flags & 0x01) != 0 && v.length >= 3 ? ((v[1] & 0xFF) | ((v[2] & 0xFF) << 8)) : (v[1] & 0xFF);
        if (value > 25 && value < 230) {
            bpm = value;
            lastMs = System.currentTimeMillis();
            status = "conectat: " + deviceName;
        }
        Log.d(TAG, "bpm=" + value);
    }
}
