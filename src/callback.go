package main

import (
	"bytes"
	"database/sql"
	"encoding/json"
	_ "github.com/go-sql-driver/mysql"
	"io/ioutil"
	"log"
	"net/http"
	"net/url"
	"time"
)

const (
	callbackUrl  = ""
	clientId     = ""
	clientSecret = ""
	dbConfig     = ""
)

var (
	db  *sql.DB
	err error
)

type DigitalOcean struct {
	AccessToken  string `json:"access_token"`
	RefreshToken string `json:"refresh_token"`
}

func callback(_ http.ResponseWriter, r *http.Request) {
	if r.URL.Query()["code"] != nil && r.URL.Query()["state"] != nil {
		token := r.URL.Query()["state"][0]

		do, _ := json.Marshal(map[string]string{
			"grant_type":    "authorization_code",
			"code":          r.URL.Query()["code"][0],
			"client_id":     clientId,
			"client_secret": clientSecret,
			"redirect_uri":  callbackUrl,
			"scope":         "read write",
			"state":         token,
		})

		res, err := http.Post("https://cloud.digitalocean.com/v1/oauth/token", "application/json", bytes.NewBuffer(do))
		if err != nil {
			log.Println(err)
			return
		}

		defer res.Body.Close()

		body, err := ioutil.ReadAll(res.Body)
		if err == nil {
			API := DigitalOcean{}
			_ = json.Unmarshal(body, &API)

			_, err := db.Exec("UPDATE DATA SET do_access = '" + API.AccessToken + "', do_refresh = '" + API.RefreshToken + "' WHERE DATA.token = '" + token + "'")

			if err == nil {
				_, err := http.PostForm("http://localhost:7777/api/auth_complete", url.Values{"token": {token}})
				if err != nil {
					log.Println(err)
				}
			}
		}
	}
}

func main() {
	db, err = sql.Open("mysql", dbConfig)
	if err != nil {
		log.Println(err)
		return
	}

	defer db.Close()

	db.SetMaxIdleConns(0)
	db.SetConnMaxLifetime(time.Second)

	http.HandleFunc("/callback", callback)

	log.Fatal(http.ListenAndServe(":1000", nil))
}
