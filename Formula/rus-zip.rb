class RusZip < Formula
  desc "Cross-platform compression suite powered by Tar+Zstandard (.zrus)"
  homepage "https://github.com/utarn/rus-zip"
  version "1.0.4"
  license "Proprietary"

  on_macos do
    # Apple Silicon only — Intel (osx-x64) builds are discontinued.
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/rus-zip-cli-osx-arm64.zip"
    sha256 "1746ce782e2dbe01a73723eac8f0642e7275f35d3a589e489caaa31e87b62aed"
  end

  on_linux do
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/rus-zip-cli-linux-x64"
    sha256 "2ec2e738b12b9c4ade69ee90ddf7dfd8e15062cf1c635a763c4c76ea6c4c3148"
  end

  def install
    if OS.mac?
      bin.install "rus-zip"
    else
      bin.install "rus-zip-cli-linux-x64" => "rus-zip"
    end
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/rus-zip --version")
  end
end
