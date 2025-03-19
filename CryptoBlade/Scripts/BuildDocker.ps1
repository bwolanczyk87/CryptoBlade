docker build -f "CryptoBlade/Dockerfile" -t cryptoblade:live .
docker save -o cryptoblade.tar cryptoblade:live