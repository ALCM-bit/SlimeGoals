// A sincronização vale em todos os builds, então o APK declara INTERNET sempre. O que fica só no desenvolvimento é o
// tráfego em texto puro (network_security_config_dev).
using Android;
using Android.App;

[assembly: UsesPermission(Manifest.Permission.Internet)]
