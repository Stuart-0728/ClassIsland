package main

import (
	"bufio"
	"bytes"
	"encoding/base64"
	"encoding/json"
	"os"
	"os/exec"
	"testing"
	"time"

	"github.com/pion/webrtc/v4"
	"github.com/pion/webrtc/v4/pkg/media"
)

func TestRtcProcess(t *testing.T) {
	if os.Getenv("BASHU_RTC_TEST_PROCESS") != "1" {
		return
	}
	main()
	os.Exit(0)
}

// Exercise ICE/DTLS/SRTP, Opus packet delivery and the exact private stdio protocol.
func TestRealPeerAudio(t *testing.T) {
	configuration := webrtc.Configuration{}
	relay := os.Getenv("BASHU_RTC_TEST_ICE") != ""
	if relay {
		if err := json.Unmarshal([]byte(os.Getenv("BASHU_RTC_TEST_ICE")), &configuration.ICEServers); err != nil {
			t.Fatal("invalid test ICE configuration")
		}
		configuration.ICETransportPolicy = webrtc.ICETransportPolicyRelay
	}
	pc, err := webrtc.NewPeerConnection(configuration)
	if err != nil {
		t.Fatal(err)
	}
	defer pc.Close()
	track, err := webrtc.NewTrackLocalStaticSample(webrtc.RTPCodecCapability{MimeType: webrtc.MimeTypeOpus, ClockRate: 48000, Channels: 2}, "voice", "test")
	if err != nil {
		t.Fatal(err)
	}
	sender, err := pc.AddTrack(track)
	if err != nil {
		t.Fatal(err)
	}
	go func() {
		buffer := make([]byte, 1500)
		for {
			if _, _, err := sender.Read(buffer); err != nil {
				return
			}
		}
	}()
	offer, err := pc.CreateOffer(nil)
	if err != nil {
		t.Fatal(err)
	}
	gathered := webrtc.GatheringCompletePromise(pc)
	if err = pc.SetLocalDescription(offer); err != nil {
		t.Fatal(err)
	}
	select {
	case <-gathered:
	case <-time.After(5 * time.Second):
		t.Fatal("sender ICE timeout")
	}
	cmd := exec.Command(os.Args[0], "-test.run=^TestRtcProcess$")
	cmd.Env = append(os.Environ(), "BASHU_RTC_TEST_PROCESS=1")
	stdin, _ := cmd.StdinPipe()
	stdout, _ := cmd.StdoutPipe()
	if err = cmd.Start(); err != nil {
		t.Fatal(err)
	}
	defer func() { _ = stdin.Close(); _ = cmd.Process.Kill(); _ = cmd.Wait() }()
	_ = json.NewEncoder(stdin).Encode(map[string]any{"offer": pc.LocalDescription().SDP, "iceServers": configuration.ICEServers, "relay": relay})
	messages := make(chan map[string]string, 128)
	go func() {
		scanner := bufio.NewScanner(stdout)
		scanner.Buffer(make([]byte, 4096), 65536)
		for scanner.Scan() {
			var message map[string]string
			if json.Unmarshal(scanner.Bytes(), &message) == nil {
				messages <- message
			}
		}
		close(messages)
	}()
	timeout := time.After(12 * time.Second)
	answered := false
	for !answered {
		select {
		case message, ok := <-messages:
			if !ok {
				t.Fatal("helper terminated")
			}
			if message["type"] == "error" {
				t.Fatal(message["message"])
			}
			if message["type"] == "answer" {
				if err = pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeAnswer, SDP: message["sdp"]}); err != nil {
					t.Fatal(err)
				}
				answered = true
			}
		case <-timeout:
			t.Fatal("answer timeout")
		}
	}
	silence := []byte{0xf8, 0xff, 0xfe} // Valid 20 ms Opus silence frame.
	ticker := time.NewTicker(20 * time.Millisecond)
	defer ticker.Stop()
	started := time.Now()
	count := 0
	for count < 10 {
		select {
		case <-ticker.C:
			if err = track.WriteSample(media.Sample{Data: silence, Duration: 20 * time.Millisecond}); err != nil {
				t.Fatal(err)
			}
		case message, ok := <-messages:
			if !ok {
				t.Fatal("receiver stopped")
			}
			if message["type"] == "audio" {
				data, err := base64.StdEncoding.DecodeString(message["data"])
				if err != nil || !bytes.Equal(data, silence) {
					t.Fatal("corrupted Opus packet")
				}
				count++
			}
			if message["type"] == "error" {
				t.Fatal(message["message"])
			}
		case <-timeout:
			t.Fatal("audio timeout")
		}
	}
	t.Logf("PASS delivered %d Opus packets over real WebRTC in %s", count, time.Since(started))
}
