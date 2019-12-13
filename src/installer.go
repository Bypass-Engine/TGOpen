package main

import (
	"context"
	"encoding/hex"
	"log"
	"math/rand"
	"net/http"
	"os"
	"os/exec"
	"time"
)

const (
	pathService = "/etc/systemd/system/TGOpen-Server.service"
	serverUrl   = ""
)

func cmd(cmd string) string {
	out, err := exec.Command("sh", "-c", cmd).Output()
	if err != nil && err.Error() != "exit status 1" && err.Error() != "exit status 2" {
		log.Println("[error]", err, "("+cmd+")")
	}

	return string(out)
}

func randomHex(n int) string {
	bytes := make([]byte, n)
	_, _ = rand.Read(bytes)
	return hex.EncodeToString(bytes)
}

func main() {
	m := http.NewServeMux()
	s := http.Server{Addr: ":1337", Handler: m}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	m.HandleFunc("/getHash", func(w http.ResponseWriter, r *http.Request) {
		rand.Seed(time.Now().UnixNano())
		hash := randomHex(12)

		file, _ := os.Create("hash")
		_, _ = file.WriteString(hash)

		_, _ = w.Write([]byte(hash))

		cancel()
	})

	go func() {
		if err := s.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatal(err)
		}
	}()

	select {
	case <-ctx.Done():
		_ = s.Shutdown(ctx)
	}

	cmd("curl -L -o tgopen-server " + serverUrl + " && chmod +x tgopen-server")

	config := `[Unit]
Description=TGOpen-Server
After=network.target
[Service]
Type=simple
WorkingDirectory=/root/
ExecStart=/root/./tgopen-server
Restart=on-failure
[Install]
WantedBy=multi-user.target`

	cmd("touch " + pathService)
	cmd("echo \"" + config + "\" >> " + pathService)

	cmd("systemctl daemon-reload")
	cmd("systemctl restart TGOpen-Server.service")
	cmd("systemctl enable TGOpen-Server.service")
}
