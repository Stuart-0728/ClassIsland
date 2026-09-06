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
	candidates := make(chan webrtc.ICECandidateInit, 32)
	pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
		if candidate != nil {
			candidates <- candidate.ToJSON()
		}
	})
	offer, err := pc.CreateOffer(nil)
	if err != nil {
		t.Fatal(err)
	}
	if err = pc.SetLocalDescription(offer); err != nil {
		t.Fatal(err)
	}
	cmd := exec.Command(os.Args[0], "-test.run=^TestRtcProcess$")
	cmd.Env = append(os.Environ(), "BASHU_RTC_TEST_PROCESS=1")
	stdin, _ := cmd.StdinPipe()
	stdout, _ := cmd.StdoutPipe()
	if err = cmd.Start(); err != nil {
		t.Fatal(err)
	}
	defer func() { _ = stdin.Close(); _ = cmd.Process.Kill(); _ = cmd.Wait() }()
	encoder := json.NewEncoder(stdin)
	_ = encoder.Encode(map[string]any{"offer": pc.LocalDescription().SDP, "offerCandidates": []any{}, "iceServers": configuration.ICEServers, "relay": relay})
	go func() {
		for candidate := range candidates {
			_ = encoder.Encode(map[string]any{"candidate": candidate})
		}
	}()
	type helperMessage struct {
		Type      string                   `json:"type"`
		Sdp       string                   `json:"sdp"`
		State     string                   `json:"state"`
		Message   string                   `json:"message"`
		Data      string                   `json:"data"`
		Candidate *webrtc.ICECandidateInit `json:"candidate"`
	}
	messages := make(chan helperMessage, 128)
	go func() {
		scanner := bufio.NewScanner(stdout)
		scanner.Buffer(make([]byte, 4096), 65536)
		for scanner.Scan() {
			var message helperMessage
			if json.Unmarshal(scanner.Bytes(), &message) == nil {
				messages <- message
			}
		}
		close(messages)
	}()
	timeout := time.NewTimer(12 * time.Second)
	defer timeout.Stop()
	answered := false
	for !answered {
		select {
		case message, ok := <-messages:
			if !ok {
				t.Fatal("helper terminated")
			}
			if message.Type == "error" {
				t.Fatal(message.Message)
			}
			if message.Type == "candidate" && message.Candidate != nil {
				if err = pc.AddICECandidate(*message.Candidate); err != nil {
					t.Fatal(err)
				}
			}
			if message.Type == "answer" {
				if err = pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeAnswer, SDP: message.Sdp}); err != nil {
					t.Fatal(err)
				}
				answered = true
			}
		case <-timeout.C:
			t.Fatal("answer timeout")
		}
	}
	timeout.Reset(12 * time.Second)
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
			if message.Type == "candidate" && message.Candidate != nil {
				if err = pc.AddICECandidate(*message.Candidate); err != nil {
					t.Fatal(err)
				}
			}
			if message.Type == "audio" {
				data, err := base64.StdEncoding.DecodeString(message.Data)
				if err != nil || !bytes.Equal(data, silence) {
					t.Fatal("corrupted Opus packet")
				}
				count++
			}
			if message.Type == "error" {
				t.Fatal(message.Message)
			}
		case <-timeout.C:
			t.Fatal("audio timeout")
		}
	}
	t.Logf("PASS delivered %d Opus packets over real WebRTC in %s", count, time.Since(started))
}
