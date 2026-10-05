# Third-party notices

AutoReShade includes or is built with the following third-party components.

## Tesseract OCR and the English language model

- Project: https://github.com/tesseract-ocr/tesseract and https://github.com/tesseract-ocr/tessdata_fast
- License: Apache License 2.0 (see [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt))
- Use: the native Tesseract library and `eng.traineddata` are embedded in AutoReShade for text recognition.

## Leptonica

- Project: http://www.leptonica.org
- License: Leptonica license (BSD 2-clause style), Copyright (C) 2001-2022 Leptonica
- Use: image library required by Tesseract, embedded in AutoReShade.

> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met: 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL ANY COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## Tesseract .NET wrapper (charlesw/tesseract)

- Project: https://github.com/charlesw/tesseract
- License: Apache License 2.0 (see [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt))

## Localized map and realm names

- Source: [dbd-map-overlay](https://github.com/LucaFontanot/dbd-map-overlay) by Luca Fontanot (`src/i18n/*.json`)
- License: Apache License 2.0 (see [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt))
- Changes: the names were reorganised into AutoReShade's `maps.json` format (grouped by realm, with ids), Roman numerals were added for the Badham Preschool variants, and names identical to English were left out.

## .NET runtime, WPF and Windows Forms

- Project: https://github.com/dotnet
- License: MIT
- Use: AutoReShade is published as a self-contained .NET application.

Dead by Daylight names are trademarks of Behaviour Interactive Inc. and are used only to identify the maps.
