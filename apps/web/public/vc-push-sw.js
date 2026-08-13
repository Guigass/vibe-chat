/**
 * Wraps Angular ngsw so Web Push does not toast the OS when a VibeChat tab
 * already has focus (B-095: in-app notice only). Click handling stays in ngsw.
 */
importScripts('./ngsw-worker.js');

const nativeShowNotification = ServiceWorkerRegistration.prototype.showNotification;

ServiceWorkerRegistration.prototype.showNotification = function (title, options) {
  return self.clients
    .matchAll({ type: 'window', includeUncontrolled: true })
    .then((windows) => {
      if (windows.some((client) => client.focused)) {
        return;
      }
      return nativeShowNotification.call(this, title, options);
    });
};
