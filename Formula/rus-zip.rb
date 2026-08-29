class RusZip < Formula
  desc "Cross-platform compression suite powered by Tar+Zstandard (.zrus)"
  homepage "https://github.com/utarn/rus-zip"
  version "1.0.4"
  license "Proprietary"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/rus-zip-cli-osx-arm64.tar.gz"
      sha256 "31ac1f280cca6b6354129d4ad1150dc9cbd629f8f4f1bf4a8d69538b4c439881"
    else
      url "https://github.com/utarn/rus-zip/releases/download/v1.0.4/rus-zip-cli-osx-x64.tar.gz"
      sha256 "a19b674ec17d3b14dfaba09d1892c62667fc45058f34e2604eba5a2f29d6d69e"
    end
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
