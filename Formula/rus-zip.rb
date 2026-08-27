class RusZip < Formula
  desc "Cross-platform compression suite powered by Tar+Zstandard (.zrus)"
  homepage "https://github.com/utarn/rus-zip"
  version "1.0.3"
  license "Proprietary"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/utarn/rus-zip/releases/download/v1.0.3/rus-zip-cli-osx-arm64.tar.gz"
      sha256 "36f88328481fa05517ad1c4c913c02f014e97209cbfcc139a8efa6f49d40c46e"
    else
      url "https://github.com/utarn/rus-zip/releases/download/v1.0.3/rus-zip-cli-osx-x64.tar.gz"
      sha256 "f22bd2add356cd9adf7541993fa48d8f8291197dc0bce9ff007cb6cc4b709d08"
    end
  end

  on_linux do
    url "https://github.com/utarn/rus-zip/releases/download/v1.0.3/rus-zip-cli-linux-x64"
    sha256 "836ddc75e93b8c53a2e1873e491ebd828f551d57e885d61148197a4f8bd0ddd4"
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
