(function (global) {
  'use strict';

  const android = global.chummerAndroid = global.chummerAndroid || {};
  let bridge = null;

  const dialogs = global.chummerDialogs = global.chummerDialogs || {};
  let revealedDialogId = null;
  let pendingDialogId = null;
  let pendingScrollOffset = null;

  function currentDialogId() {
    return document.getElementById('dialogBackdrop')?.getAttribute('data-dialog-id') || null;
  }

  dialogs.isSameDialogRefresh = function (dialogId) {
    return !!dialogId && (dialogId === revealedDialogId || dialogId === pendingDialogId);
  };

  dialogs.revealActiveDialog = function () {
    return new Promise((resolve) => {
      const reveal = function () {
        const backdrop = document.getElementById('dialogBackdrop');
        if (!backdrop) {
          revealedDialogId = null;
          resolve();
          return;
        }

        const dialogId = currentDialogId();
        if (!dialogs.isSameDialogRefresh(dialogId)) {
          backdrop.scrollIntoView({ block: 'center', inline: 'center' });
        }

        revealedDialogId = dialogId;
        resolve();
      };

      if (typeof global.requestAnimationFrame === 'function') {
        global.requestAnimationFrame(reveal);
      } else {
        global.setTimeout(reveal, 0);
      }
    });
  };

  dialogs.captureDialogScroll = function (element) {
    if (!element) {
      pendingDialogId = null;
      pendingScrollOffset = [0, 0];
      return pendingScrollOffset;
    }

    pendingDialogId = currentDialogId();
    pendingScrollOffset = [element.scrollTop || 0, element.scrollLeft || 0];
    return pendingScrollOffset;
  };

  dialogs.restoreDialogScroll = function (element, scrollOffset) {
    if (!element || !Array.isArray(scrollOffset) || scrollOffset.length < 2) return;

    const top = Number(scrollOffset[0] || 0);
    const left = Number(scrollOffset[1] || 0);
    const restore = function () {
      element.scrollTop = top;
      element.scrollLeft = left;
    };

    restore();
    global.setTimeout(restore, 0);
    global.setTimeout(restore, 48);
    global.setTimeout(restore, 160);
  };

  dialogs.restorePendingDialogScroll = function (element, dialogId) {
    if (!element
      || !Array.isArray(pendingScrollOffset)
      || pendingScrollOffset.length < 2
      || (!!pendingDialogId && !!dialogId && pendingDialogId !== dialogId)) {
      return false;
    }

    dialogs.restoreDialogScroll(element, pendingScrollOffset);
    return true;
  };

  function requireBridge() {
    if (!bridge) throw new Error('The Android native bridge is not ready.');
    return bridge;
  }

  android.initialize = function (dotNetBridge) {
    bridge = dotNetBridge;

    const downloads = global.chummerDownloads = global.chummerDownloads || {};
    downloads.downloadBase64 = function (fileName, contentBase64, mimeType) {
      return requireBridge().invokeMethodAsync(
        'SaveBase64Async',
        fileName || 'chummer-character.chum5',
        contentBase64 || '',
        mimeType || 'application/octet-stream');
    };
    downloads.saveRecoveryStream = async function (
      fileName,
      mimeType,
      documentLength,
      exportToken,
      streamReference) {
      const outcome = (status, error) => Object.freeze({ status, error: error || null });
      try {
        const buffer = await streamReference.arrayBuffer();
        if (!(buffer instanceof ArrayBuffer) || buffer.byteLength !== documentLength) {
          return outcome('stale', 'Recovery byte length changed before save.');
        }
        const bytes = new Uint8Array(buffer);
        let binary = '';
        const chunkSize = 0x8000;
        for (let offset = 0; offset < bytes.length; offset += chunkSize) {
          binary += String.fromCharCode.apply(null, bytes.subarray(offset, offset + chunkSize));
        }
        const saved = await requireBridge().invokeMethodAsync(
          'SaveBase64Async',
          fileName,
          btoa(binary),
          mimeType);
        bytes.fill(0);
        binary = '';
        return outcome(saved ? 'durable_saved' : 'cancelled');
      } catch (error) {
        return outcome('failed', error && error.message ? error.message : 'Android save failed.');
      }
    };

    const exports = global.chummerExports = global.chummerExports || {};
    exports.downloadBase64 = downloads.downloadBase64;

    const prints = global.chummerPrints = global.chummerPrints || {};
    prints.openBase64 = function (fileName, contentBase64, mimeType, title) {
      return requireBridge().invokeMethodAsync(
        'PrintBase64Async',
        fileName || 'chummer-character.pdf',
        contentBase64 || '',
        mimeType || 'application/pdf',
        title || 'Chummer character');
    };
  };

  android.dispose = function () {
    bridge = null;
  };
})(window);
