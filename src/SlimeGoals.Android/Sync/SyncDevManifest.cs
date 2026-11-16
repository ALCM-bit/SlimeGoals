// Só o APK de desenvolvimento fala com a rede (decisão da 9.5a, mantida na 9.5b): o de distribuição não declara INTERNET.
#if SYNC_DEV
using Android;
using Android.App;

[assembly: UsesPermission(Manifest.Permission.Internet)]
#endif
