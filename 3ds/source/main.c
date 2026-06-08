#include <3ds.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <malloc.h>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <unistd.h>
#include <fcntl.h>
#include <netdb.h>

#define CONFIG_FILE "sdmc:/config.ini" 
#define MAX_LINE 256
#define MAX_CONNECTIONS 15

typedef struct __attribute__((packed)) {
    u32 buttons;            
    circlePosition circlepad; 
    circlePosition cstick;  
    s16 gyroX; 
    s16 gyroY;
    s16 gyroZ;
} controllerstate;

typedef struct {
    char serverip[64];
    int port;
} config_entry;

config_entry connection_list[MAX_CONNECTIONS];
int connection_count = 0;
int selected_index = 0;

void load_config_list() {
    connection_count = 0;
    FILE *file = fopen(CONFIG_FILE, "r");
    if (!file) {
        file = fopen(CONFIG_FILE, "w");
        if (file) {
            fprintf(file, "192.168.1.1:8888\n");
            fclose(file);
            strcpy(connection_list[0].serverip, "192.168.1.1");
            connection_list[0].port = 8888;
            connection_count = 1;
        }
        return;
    }

    char line[MAX_LINE];
    while (fgets(line, MAX_LINE, file) && connection_count < MAX_CONNECTIONS) {
        size_t len = strlen(line);
        if (len > 0 && line[len-1] == '\n') line[len-1] = '\0';
        if (len > 0 && line[len-2] == '\r') line[len-2] = '\0'; 

        if (strlen(line) == 0) continue;

        char *ip = strtok(line, ":");
        char *port_str = strtok(NULL, ":");

        if (ip && port_str) {
            strncpy(connection_list[connection_count].serverip, ip, 63);
            connection_list[connection_count].port = atoi(port_str);
            connection_count++;
        }
    }
    fclose(file);
}

void save_config_list() {
    FILE *file = fopen(CONFIG_FILE, "w");
    if (!file) return;

    for (int i = 0; i < connection_count; i++) {
        fprintf(file, "%s:%d\n", connection_list[i].serverip, connection_list[i].port);
    }
    fclose(file);
}

void menu_add_connection() {
    static SwkbdState swkbd;
    char mybuf[64];
    
    swkbdInit(&swkbd, SWKBD_TYPE_NORMAL, 2, 15);
    swkbdSetValidation(&swkbd, SWKBD_NOTEMPTY_NOTBLANK, 0, 0);
    swkbdSetHintText(&swkbd, "Enter IP (e.g. 192.168.x.x)");
	
    if (swkbdInputText(&swkbd, mybuf, sizeof(mybuf)) != SWKBD_BUTTON_CONFIRM) {
        return; 
    }

    char ip_res[64];
    strcpy(ip_res, mybuf);

    swkbdInit(&swkbd, SWKBD_TYPE_NORMAL, 2, 5);
    swkbdSetValidation(&swkbd, SWKBD_NOTEMPTY_NOTBLANK, 0, 0);
    swkbdSetHintText(&swkbd, "Enter Port (e.g. 8888)");
	
    if (swkbdInputText(&swkbd, mybuf, sizeof(mybuf)) != SWKBD_BUTTON_CONFIRM) {
        return; 
    }

    int port_res = atoi(mybuf);

    if (connection_count < MAX_CONNECTIONS) {
        strcpy(connection_list[connection_count].serverip, ip_res);
        connection_list[connection_count].port = port_res;
        connection_count++;
        save_config_list();
    }
}

void menu_delete_connection() {
    if (connection_count <= 0) return;

    for (int i = selected_index; i < connection_count - 1; i++) {
        connection_list[i] = connection_list[i + 1];
    }
    connection_count--;
    save_config_list();

    if (selected_index >= connection_count && selected_index > 0) {
        selected_index--;
    }
}

void draw_menu() {
    consoleClear();
    printf("\x1b[1;4H+---------------------------------------+");
    printf("\x1b[2;4H|           SELECT CONNECTION           |");
    printf("\x1b[3;4H+---------------------------------------+");
	printf("\x1b[4;4H|                                       |");
    printf("\x1b[5;4H| [Y] Add New Connect                   |");
    printf("\x1b[6;4H| [X] Delete Selected                   |");
    printf("\x1b[7;4H+---------------------------------------+");

    int start_row = 9;
    if (connection_count == 0) {
        printf("\x1b[9;14HNo connections found.");
    } else {
        for (int i = 0; i < connection_count; i++) {
            if (i == selected_index) {
                printf("\x1b[%d;2H-> %s:%d", start_row + i, connection_list[i].serverip, connection_list[i].port);
            } else {
                printf("\x1b[%d;5H%s:%d", start_row + i, connection_list[i].serverip, connection_list[i].port);
            }
        }
    }

    printf("\x1b[28;4H+---------------------------------------+");
    printf("\x1b[29;4H| D-Pad: Nav | A: Connect | START: Exit |");
    printf("\x1b[30;4H+---------------------------------------+");
}

int initsocket(const config_entry *cfg) {
    int sockfd;
    struct sockaddr_in serveraddr;

    if ((sockfd = socket(AF_INET, SOCK_DGRAM, 0)) < 0) return -1;

    memset(&serveraddr, 0, sizeof(serveraddr));
    serveraddr.sin_family = AF_INET;
    serveraddr.sin_port = htons(cfg->port);
    
    if (inet_pton(AF_INET, cfg->serverip, &serveraddr.sin_addr) <= 0) {
        close(sockfd);
        return -1;
    }
    
    if (connect(sockfd, (struct sockaddr*)&serveraddr, sizeof(serveraddr)) < 0) {
        close(sockfd);
        return -1;
    }
    
    int flags = fcntl(sockfd, F_GETFL, 0);
    fcntl(sockfd, F_SETFL, flags | O_NONBLOCK);
    
    return sockfd;
}

int checkconnection(int sockfd) {
    if (sockfd < 0) return 0;
    char ping_buf[6] = "ping";
    char pong_buf[6] = {0};
    
    if (send(sockfd, ping_buf, 5, 0) <= 0) return 0;
    
    fd_set readfds;
    struct timeval timeout;
    FD_ZERO(&readfds);
    FD_SET(sockfd, &readfds);
    timeout.tv_sec = 0;
    timeout.tv_usec = 100000;
    
    if (select(sockfd + 1, &readfds, NULL, NULL, &timeout) > 0) {
        ssize_t received = recv(sockfd, pong_buf, sizeof(pong_buf) - 1, 0);
        if (received > 0) {
            pong_buf[received] = '\0';
            if (strcmp(pong_buf, "pong") == 0) return 1;
        }
    }
    return 0;
}

int sendcontrollerstate(int sockfd, controllerstate *state) {
    return send(sockfd, state, sizeof(controllerstate), 0);
}

void getbatterystatus(char *buffer, size_t size) {
    u8 percentage, charging;
    PTMU_GetBatteryLevel(&percentage);
    PTMU_GetBatteryChargeState(&charging);
    
    int actualpercentage = (percentage + 1) * 20;
    if (actualpercentage > 100) actualpercentage = 100;
    
    if (charging) snprintf(buffer, size, "%d%% CHG", actualpercentage);
    else snprintf(buffer, size, "%d%%", actualpercentage);
}

void printstatusmessage(int sockfd, const config_entry *cfg, int isconnected, int lcdstate) {
    consoleClear();
    char batterystatus[32];
    getbatterystatus(batterystatus, sizeof(batterystatus));
    
    printf("\x1b[8;6H+-------------------------------------+");
    printf("\x1b[9;6H|          \x1b[1;36mYaPiDoor controll\x1b[0m          |");
    printf("\x1b[10;6H+-------------------------------------+");
    
    if (isconnected) 
        printf("\x1b[11;6H| Status:   \x1b[32mCONNECTED\x1b[0m                 |");
    else 
        printf("\x1b[11;6H| Status:   \x1b[31mWAITING FOR CONNECTION\x1b[0m    |");
    
    printf("\x1b[12;6H| IP:       %-25s |", cfg->serverip);
	printf("\x1b[13;6H| Port:     %-25d |", cfg->port);
	printf("\x1b[14;6H| Battery:  %-25s |", batterystatus);
    
    printf("\x1b[15;6H+-------------------------------------+");
    printf("\x1b[16;6H|              \x1b[1;33mCONTROLS\x1b[0m               |");
	printf("\x1b[17;6H|                                     |");
    printf("\x1b[18;6H| Hold L+R:         Toggle LCD        |");
    printf("\x1b[19;6H| LEFT+B+SELECT:    Back to menu      |"); 
    printf("\x1b[20;6H| START+SELECT:     Exit              |");
    printf("\x1b[21;6H+-------------------------------------+");
}

int main(int argc, char **argv) {
    gfxInitDefault();
    
    consoleInit(GFX_TOP, NULL);
    
    socInit((u32*)memalign(0x1000, 0x100000), 0x100000);
    ptmuInit();
    HIDUSER_EnableGyroscope(); 
    
    load_config_list();
    
    bool exit_app = false;
    
    draw_menu(); 

    while (aptMainLoop() && !exit_app) {
        hidScanInput();
        u32 kDown = hidKeysDown();
        bool menu_needs_update = false;

        if (kDown & KEY_START) {
            exit_app = true;
            break;
        }
        if (kDown & KEY_UP) {
            selected_index--;
            if (selected_index < 0) selected_index = (connection_count > 0) ? connection_count - 1 : 0;
            menu_needs_update = true;
        }
        if (kDown & KEY_DOWN) {
            selected_index++;
            if (selected_index >= connection_count) selected_index = 0;
            menu_needs_update = true;
        }
        if (kDown & KEY_Y) {
            menu_add_connection();
            menu_needs_update = true;
        }
        if (kDown & KEY_X) {
            menu_delete_connection();
            menu_needs_update = true;
        }

        if ((kDown & KEY_A) && connection_count > 0) {
            config_entry active_cfg = connection_list[selected_index];
            
            consoleClear();
            printf("Connecting to %s:%d...\n", active_cfg.serverip, active_cfg.port);
            
            int sockfd = initsocket(&active_cfg);
            int isconnected = 0;
            int lcdstate = 1;
            bool combo_pressed = false;
			u64 hold_start_time = 0;
			bool screens_on = true;
            u32 connectionchecktime = 0;
            u32 laststatusupdate = 0;

            while (aptMainLoop()) {
                hidScanInput();
                u32 keysheld = hidKeysHeld();
                
                if ((keysheld & KEY_DLEFT) && (keysheld & KEY_B) && (keysheld & KEY_SELECT)) {
                    if (sockfd >= 0) close(sockfd);
                    break; 
                }

                u32 currenttime = osGetTime();
                
                if (currenttime - connectionchecktime > 1000) {
                    isconnected = checkconnection(sockfd);
                    connectionchecktime = currenttime;
                    
                    if (!isconnected && sockfd >= 0) {
                        close(sockfd);
                        sockfd = -1;
                    } else if (!isconnected && sockfd < 0) {
                        sockfd = initsocket(&active_cfg);
                    }
                }
                
                if ((keysheld & KEY_L) && (keysheld & KEY_R)) {
                    if (!combo_pressed) {
						combo_pressed = true;
						hold_start_time = osGetTime();
					} else {
                        if (osGetTime() - hold_start_time >= 1000) {
							if (screens_on) {
							GSPLCD_PowerOffBacklight(GSPLCD_SCREEN_BOTH);
							screens_on = false;
                        } else {
                            gspLcdInit();
							GSPLCD_PowerOnBacklight(GSPLCD_SCREEN_BOTH);
							screens_on = true;
                        }
						combo_pressed = false;
						hold_start_time = osGetTime() + 999999;
						}
                    }
                } else {
                    combo_pressed = false;
                }
				
				if ((keysheld & KEY_START) && (keysheld & KEY_SELECT)) {
                    
					if (!screens_on) {
						gspLcdInit();
						GSPLCD_PowerOnBacklight(GSPLCD_SCREEN_BOTH);
						screens_on = true;						
					}
					
					exit_app = true;
                    if (sockfd >= 0){
						close(sockfd);
					}
					
                    break;
                }
                
                controllerstate state;
                memset(&state, 0, sizeof(controllerstate));
                state.buttons = keysheld;
				
				circlePosition temp_pos;
				hidCircleRead(&temp_pos);
				state.circlepad = temp_pos;
				                
                circlePosition temp_cstick;
                hidCstickRead(&temp_cstick);
                state.cstick = temp_cstick;
                
                angularRate gyro_data;
                hidGyroRead(&gyro_data); 
                state.gyroX = gyro_data.x;
                state.gyroY = gyro_data.y;
                state.gyroZ = gyro_data.z;
                
                if (isconnected && sockfd >= 0) {
                    if (sendcontrollerstate(sockfd, &state) < 0) {
                        isconnected = 0;
                    }
                }
                
                if (currenttime - laststatusupdate > 1000) {
                    printstatusmessage(sockfd, &active_cfg, isconnected, lcdstate);
                    laststatusupdate = currenttime;
                }
                
                gfxFlushBuffers();
                gfxSwapBuffers();
                gspWaitForVBlank();
                svcSleepThread(16666666);
            }
            
            menu_needs_update = true;
        }

        if (menu_needs_update) {
            draw_menu();
        }

        gfxFlushBuffers();
        gfxSwapBuffers();
        gspWaitForVBlank();
    }
    
    HIDUSER_DisableGyroscope(); 
    ptmuExit();
    socExit();
    gfxExit();
    return 0;
}
