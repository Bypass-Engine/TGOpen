package main

import (
	"encoding/hex"
	"encoding/json"
	"io/ioutil"
	"log"
	"math/rand"
	"net"
	"net/http"
	"os"
	"os/exec"
	"regexp"
	"strconv"
	"strings"
	"time"

	"github.com/gorilla/mux"
)

const (
  serverPort = 1337
	version    = 00000000
)

// Server structure
type Server struct {
	IP     string `json:"ip"`
	Port   string `json:"port"`
	Secret string `json:"secret"`
}

type allServers []Server

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

func getIP() string {
	conn, err := net.Dial("udp", "8.8.8.8:80")

	defer func() {
		err := conn.Close()
		if err != nil {
			log.Println("[error]", err)
		}
	}()

	ip := conn.LocalAddr().(*net.UDPAddr).IP.String()

	if net.ParseIP(ip) != nil && err == nil {
		return ip
	}

	return ""
}

func getHash() string {
	out, err := ioutil.ReadFile("./hash")
	if err != nil {
		return ""
	}

	return string(out)
}

func getVersion(w http.ResponseWriter, _ *http.Request) {
	_, _ = w.Write([]byte(strconv.Itoa(version)))
}

func getServers(w http.ResponseWriter, _ *http.Request) {
	files, err := ioutil.ReadDir("/etc/systemd/system/")
	if err != nil {
		log.Fatal(err)
	}

	servers := allServers{}

	for _, f := range files {
		valid := regexp.MustCompile(`(?m)(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}`)

		for range valid.FindAllString(f.Name(), -1) {
			part := strings.Split(f.Name(), "-")
			part[2] = strings.TrimRight(part[2], ".service")

			servers = append(servers, Server{
				IP:     part[0],
				Port:   part[1],
				Secret: part[2],
			})
		}
	}

	_ = json.NewEncoder(w).Encode(servers)
}

func createServer(w http.ResponseWriter, _ *http.Request) {
	ip := getIP()
	domainHex := "7777772e676f6f676c652e636f6d" // www.google.com

	var port string

	for {
		rand.Seed(time.Now().UnixNano())
		port = strconv.Itoa(rand.Intn(65535-21845) + 21845)

		check := cmd("netstat -tulpn | grep :" + port)

		if check == "" {
			break
		}
	}

	secret := randomHex(16)
	path := "/etc/systemd/system/" + ip + "-" + port + "-ee" + secret + domainHex + ".service"

	if _, err := os.Stat("/opt/mtproxy/"); os.IsNotExist(err) {
		cmd("apt update")
		cmd("apt -y install git make build-essential libssl-dev zlib1g-dev")

		cmd("git clone https://github.com/hookzof/MTProxy && cd MTProxy && make && cd objs/bin && " +
			"curl -s https://core.telegram.org/getProxySecret -o proxy-secret && " +
			"curl -s https://core.telegram.org/getProxyConfig -o proxy-multi.conf")

		cmd("cd /opt && mkdir mtproxy")
		cmd("cp MTProxy/objs/bin/mtproto-proxy /opt/mtproxy/mtproto-proxy")
		cmd("cp MTProxy/objs/bin/proxy-multi.conf /opt/mtproxy/proxy-multi.conf")
		cmd("cp MTProxy/objs/bin/proxy-secret /opt/mtproxy/proxy-secret")

		cmd("rm -r MTProxy")
	}

	cmd("touch " + path)

	config := `[Unit]
Description=MTProxy
After=network.target
[Service]
Type=simple
WorkingDirectory=/opt/mtproxy
ExecStart=/opt/mtproxy/mtproto-proxy -u nobody -H ` + port + " -S " + secret + ` -D www.google.com --aes-pwd proxy-secret proxy-multi.conf
Restart=on-failure
LimitNOFILE=infinity
LimitMEMLOCK=infinity
[Install]
WantedBy=multi-user.target`

	cmd("echo \"" + config + "\" >> " + path)

	cmd("systemctl daemon-reload")
	cmd("systemctl restart " + ip + "-" + port + "-ee" + secret + domainHex + ".service")
	cmd("systemctl enable " + ip + "-" + port + "-ee" + secret + domainHex + ".service")

	server := allServers{}

	server = append(server, Server{
		IP:     ip,
		Port:   port,
		Secret: "ee" + secret + domainHex,
	})

	_ = json.NewEncoder(w).Encode(server)
}

func deleteServer(w http.ResponseWriter, r *http.Request) {
	servers := allServers{}

	port := mux.Vars(r)["port"]
	ip := getIP()

	res, err := http.Get("http://" + ip + ":" + strconv.Itoa(serverPort) + "/" + getHash() + "/getServers")
	if err != nil {
		log.Fatal(err)
	}

	out, err := ioutil.ReadAll(res.Body)
	if err != nil {
		log.Fatal(err)
	}

	_ = json.Unmarshal(out, &servers)

	for _, v := range servers {
		if v.Port == port {
			cmd("systemctl stop " + v.IP + "-" + v.Port + "-" + v.Secret + ".service")
			cmd("systemctl disable " + v.IP + "-" + v.Port + "-" + v.Secret + ".service")
			cmd("rm /etc/systemd/system/" + v.IP + "-" + v.Port + "-" + v.Secret + ".service")

			_, _ = w.Write([]byte("ok"))
			return
		}
	}

	_, _ = w.Write([]byte("error"))
}

func main() {
	hash := getHash()

	router := mux.NewRouter().StrictSlash(true)

	router.HandleFunc("/"+hash+"/getServers", getServers).Methods("GET")
	router.HandleFunc("/"+hash+"/createServer", createServer).Methods("GET")
	router.HandleFunc("/"+hash+"/deleteServer/{port}", deleteServer).Methods("GET")

	router.HandleFunc("/"+hash+"/getVersion", getVersion).Methods("GET")

	log.Fatal(http.ListenAndServe(":"+strconv.Itoa(serverPort), router))
}
