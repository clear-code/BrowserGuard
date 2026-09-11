# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at http://mozilla.org/MPL/2.0/.

DATE=$(shell date +%Y%m%d)
BASE=$(shell pwd)
OUTDIR=$(BASE)/../outs

# How to generate a PDF document:
#
#   $ sudo apt install texlive-full pandoc fonts-noto-cjk
#   $ make (Name of Document)-pdf
#
PANDOC_OPT_PDF= -N --toc-depth=2 --table-of-contents \
                -f markdown+escaped_line_breaks \
                --wrap=preserve \
                --template=eisvogel.latex \
				--lua-filter=line-break.lua \
                -V documentclass=ltjsarticle \
                -V classoption=titlepage \
                -V header-right=" " \
                -V table-use-row-colors=true \
                -H deeplists.tex \
                -H codeblocks.tex \
                --pdf-engine=xelatex

all: BrowserGuardAdminGuide BrowserGuardUserGuide

BrowserGuardAdminGuide: BrowserGuardAdminGuide-pdf 

BrowserGuardAdminGuide-pdf:
	test -d "$(OUTDIR)" || mkdir "$(OUTDIR)" 
	cat BrowserGuardAdminGuide.md | pandoc ${PANDOC_OPT_PDF} -o "$(OUTDIR)/BrowserGuardAdminGuide-$(DATE).pdf"
clean:
	rm -rf $(OUTDIR)

BrowserGuardUserGuide: BrowserGuardUserGuide-pdf 

BrowserGuardUserGuide-pdf:
	test -d "$(OUTDIR)" || mkdir "$(OUTDIR)" 
	cat BrowserGuardUserGuide.md | pandoc ${PANDOC_OPT_PDF} -o "$(OUTDIR)/BrowserGuardUserGuide-$(DATE).pdf"
clean:
	rm -rf $(OUTDIR)
