'use strict';

export const UploadFileBridge = {
  SERVER_NAME: 'com.clear_code.upload_file_bridge',
  async bridgeFile(path) {
    const query = new String('S ' + path);
    try {
      const resp = await chrome.runtime.sendNativeMessage(SERVER_NAME, query);
      if (!resp) {
        console.log('No response from native host', query);
      }
      else if (resp.result === 'OK') {
        console.log('File copied successfully', query);
      } else {
        console.log('Failed to copy file', query, resp);
      }
    } catch (e) {
      console.log('Cannot copy file', query, e.message);
    }
  }
}
