.PHONY: clean zip dev

JS_FILES = background.js \
           upload-guard.js \
           upload-file-bridge.js \
           script-executor.js \
           startup-launcher.js \
           net-logger.js \
           block-setting-page.js \
           content.js

FILES = manifest.json \
        misc/128x128.png \
        _locales/ja/messages.json \
        $(JS_FILES)

all: zip dev

clean:
	rm -f *.zip

zip: $(FILES)
	zip -9 - $(FILES) > BrowserGuardEdge.zip

dev: $(FILES)
	rm -rf dev
	mkdir -p dev
	cp -R manifest.json misc _locales $(JS_FILES) dev/
	sed -i -E -e 's/BrowserGuard/BrowserGuard Enterprise Developer Edition/g' dev/_locales/*/messages.json
	cd dev && zip -9 - $(FILES) > ../BrowserGuardEdgeDev.zip
