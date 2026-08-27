cask "rus-zip" do
  version "1.0.3"

  if Hardware::CPU.arm?
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.3/RusZip-mac-arm64.zip"
    sha256 "c262ff9c92338c44af0c0eaf1ec9742e2c876b8201f50771f56fe2bf95c119cc"
  else
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.3/RusZip-mac-x64.zip"
    sha256 "9750e286f53ba47f220227f905489b8ba41628125a2fdacaa8995944f1e5dec4"
  end

  name "RUS ZIP"
  desc "Modern cross-platform archive utility powered by Tar+Zstandard (.zrus) and Avalonia"
  homepage "https://github.com/utarn/rus-zip"

  app "RusZip.app"
  binary "#{appdir}/RusZip.app/Contents/MacOS/RusZip", target: "ruszip"

  zap trash: [
    "~/.config/rus-zip",
    "~/Library/Application Support/RUS ZIP",
    "~/Library/Preferences/com.ruszip.desktop.plist"
  ]
end
