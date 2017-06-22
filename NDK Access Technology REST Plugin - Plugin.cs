using System;
using System.Linq;
using NDK.Framework;
using RestSharp;
using AcctPublicRestCommunicationLibrary;
using System.Globalization;
using System.Collections.Generic;
using System.Net;

namespace NDK.AcctPlugin {

	#region DemoPlugin class.
	public class AcctPlugin : BasePlugin {

		#region Implement PluginBase abstraction.
		/// <summary>
		/// Gets the unique plugin guid.
		/// When implementing a plugin, this method should return the same unique guid every time. 
		/// </summary>
		/// <returns></returns>
		public override Guid GetGuid() {
			return new Guid("{0F51482D-EADC-489F-A2BB-6746A2D42B90}");
		} // GetGuid

		/// <summary>
		/// Gets the the plugin name.
		/// When implementing a plugin, this method should return a proper display name.
		/// </summary>
		/// <returns></returns>
		public override String GetName() {
			return "NDK Synchronize to Access Technology REST service.";
		} // GetName

		/// <summary>
		/// Run the plugin.
		/// When implementing a plugin, this method is invoked by the service application or the commandline application.
		/// 
		/// If the method finishes when invoked by the service application, it is reinvoked after a short while as long as the
		/// service application is running.
		/// 
		/// Take care to write good comments in the code, log as much as possible, as correctly as possible (little normal, much debug).
		/// </summary>
		public override void Run() {
			try {
				// Get config.
				Boolean configMessageSend = this.GetLocalValue("MessageSend", true);
				List<String> configMessageTo = this.GetLocalValues("MessageTo");
				String configMessageSubject = this.GetLocalValue("MessageSubject", this.GetName());

				String groupNameCareAccessUser = this.GetLocalValue("GroupUser", String.Empty);
				AdGroup groupCareAccessUser = this.GetGroup(groupNameCareAccessUser);
				Boolean syncDeleteDisabledUsers = this.GetLocalValue("DeleteDisabledUsers", true);

				List<String> sofdJobTitleIds = this.GetLocalValues("SofdJobTitleIds");					// stillingsId.
				List<String> sofdJobTitleNames = this.GetLocalValues("SofdJobTitleNames");				// stillingsBetegnelse.
				List<String> sofdOrganizationIds = this.GetLocalValues("SofdOrganizationIds");			// organisationId.
				List<String> sofdOrganizationNames = this.GetLocalValues("SofdOrganizationNames");		// organisationNavn.
				List<String> sofdPayClassNames = this.GetLocalValues("SofdPayClassNames");				// loenKlasse.

				Boolean failOnNoUsers = this.GetLocalValue("FailOnNoUsers", true);

				String hostAddress = this.GetLocalValue("AcctHostAddress", "http://test.acct.dk/rest/current");
				Uri hostUrlQueryUsers = new Uri(hostAddress.TrimEnd('/') + "/users?includeDeleted=false");
				Uri hostUrlSynchronize = new Uri(hostAddress.TrimEnd('/') + "/users/synchronize");
				String userName = this.GetLocalValue("AcctUserName", String.Empty);
				String userPassword = this.GetLocalValue("AcctUserPassword", String.Empty);
				String syncEvaluationType = this.GetLocalValue("AcctUserEvaluationType", "TEST");
				String syncLimitToZone = this.GetLocalValue("AcctUserLimitToZone", String.Empty);
				String syncIgnorePidRegEx = this.GetLocalValue("AcctUserPidIgnoreRegex", String.Empty);
				List<String> syncIgnorePid = this.GetLocalValues("AcctUserPidIgnoreList");
				Boolean syncIgnoreCase = this.GetLocalValue("AcctUserPidIgnoreCase", true);
				Boolean syncAllowPidNull = this.GetLocalValue("AcctUserPidAllowNull", true);
				Int32 syncMaximumLevel = this.GetLocalValue("AcctUserMaximumLevel", 5);
				Method syncRestMethod = Method.PUT;

				Boolean queryUserMiFareIdAD = this.GetLocalValue("QueryUserMiFareIdAD", false);
				Boolean queryUserMiFareIdOverrideAD = this.GetLocalValue("QueryUserMiFareIdOverrideAD", false);
				Boolean queryUserMiFareIdSOFD = this.GetLocalValue("QueryUserMiFareIdSOFD", false);
				Boolean queryUserMiFareIdOverrideSOFD = this.GetLocalValue("QueryUserMiFareIdOverrideSOFD", false);
				Method queryUserRestMethod = Method.GET;

				// Validate group.
				if (groupCareAccessUser == null) {
					throw new Exception(String.Format("The group '{0}' was not found in the Active Directory.", groupNameCareAccessUser));
				}

				// Validate password.
				if ((userName.Any(x => x > 127) == true) || (userPassword.Any(x => x > 127) == true)) {
					throw new Exception("Username and password may only contain ASCII characters.");
				}

				// Validate maximum level.
				if (syncMaximumLevel < 0) {
					syncMaximumLevel = 0;
				}



				//-----------------------------------------------------------------------------------------------------------------------------------
				// Query users.
				// Copy MiFare identifiers back to Active Directory and SOFD Directory.
				//-----------------------------------------------------------------------------------------------------------------------------------
				List<AdUser> queryUserUpdatedAD = new List<AdUser>();					// For report building.
				List<SofdEmployee> queryUserUpdatedSOFD = new List<SofdEmployee>();		// For report building.
				if ((queryUserMiFareIdAD == true) || (queryUserMiFareIdSOFD == true)) {
					// Create the REST client.
					RestClient restClientUsers = new RestClient(hostUrlQueryUsers.Scheme + "://" + hostUrlQueryUsers.Host + (hostUrlQueryUsers.IsDefaultPort ? "" : ":" + hostUrlQueryUsers.Port.ToString(CultureInfo.InvariantCulture)));
					restClientUsers.Authenticator = new HttpBasicAuthenticator(userName, userPassword);

					// Create REST request.
					RestRequest restRequestUsers = new RestRequest(hostUrlQueryUsers.PathAndQuery, queryUserRestMethod);
					restRequestUsers.XmlSerializer = new XmlDataContractSerializer();
					restRequestUsers.RequestFormat = DataFormat.Xml;

					restClientUsers.AddHandler("text/xml", new XmlDataContractSerializer());
					restClientUsers.AddHandler("application/xml", new XmlDataContractSerializer());

					// Send the request.
					IRestResponse<UserCollection> restResponseUsers = restClientUsers.Execute<UserCollection>(restRequestUsers);
					if (restResponseUsers.StatusCode == HttpStatusCode.OK) {
						foreach (User user in restResponseUsers.Data) {
							if ((user.Pid.IsNullOrWhiteSpace() == false) &&
								(user.Pid.Trim().Length > 3) &&
								((user.Pid.StartsWith("AD-") == true) || (user.Pid.StartsWith("MA-") == true)) &&
								(user.Card.IsNullOrWhiteSpace() == false)) {
								// Get the associated user from Active Directory and SOFD Directory.
								AdUser adUser = this.GetUser(user.Pid.Substring(3));
								String adUserMiFareId = this.GetUserMiFareId(adUser);
								SofdEmployee employee = this.GetEmployee(user.Pid.Substring(3));
								if ((adUser != null) && (employee == null)) {
									employee = this.GetEmployee(adUser.SamAccountName);
								}
								if ((adUser == null) && (employee != null)) {
									adUser = this.GetUser(employee.AdBrugerNavn);
									adUserMiFareId = this.GetUserMiFareId(adUser);
								}

								// Update Active Directory user.
								if ((queryUserMiFareIdAD == true) &&
									(adUser != null) &&
									((queryUserMiFareIdOverrideAD == true) || (adUserMiFareId.IsNullOrWhiteSpace() == true))) {
									// Update the user.
									adUserMiFareId = user.Card.Trim();
									this.SetUserMiFareId(adUser, adUserMiFareId);
									adUser.Save();

									// Add to the report.
									queryUserUpdatedAD.Add(adUser);

									// Log.
									this.Log("Updated MiFareId: {0}, {1}. MiFareId: {2}", adUser.SamAccountName, adUser.Name, adUserMiFareId);
								}

								// Update SOFD Directory user.
								if ((queryUserMiFareIdSOFD == true) &&
									(employee != null) &&
									((queryUserMiFareIdOverrideSOFD == true) || (employee.MiFareId.IsNullOrWhiteSpace() == true))) {
									// Update the employee.
									employee.MiFareId = user.Card.Trim();
									employee.Save(true);

									// Add to the report.
									queryUserUpdatedSOFD.Add(employee);

									// Log.
									this.Log("Updated MiFareId: {0}, {1}. MiFareId: {2}", employee.MaNummer, employee.Navn, employee.MiFareId);
								}
							}
						}

						// Log.
						this.LogDebug("Query user: Found {0} users", restResponseUsers.Data.Count);
					} else {
						// Log.
						this.LogError("Query user error: {0} - {1}. {2}", restResponseUsers.StatusCode, restResponseUsers.StatusDescription, restResponseUsers.ErrorMessage);
					}
				}



				//-----------------------------------------------------------------------------------------------------------------------------------
				// Synchronize.
				//-----------------------------------------------------------------------------------------------------------------------------------
				// Setup synchronization evaluator.
				EvaluateUserCollection usercol = new EvaluateUserCollection();
				switch (syncEvaluationType.Trim().ToLower()) {
					case "add":
						usercol.EvaluationType = UserEvaluationType.AddNewUsers;
						break;
					case "remove":
						usercol.EvaluationType = UserEvaluationType.RemoveNotPresent;
						break;
					case "addremove":
						usercol.EvaluationType = UserEvaluationType.AddNewUsers | UserEvaluationType.RemoveNotPresent;
						break;
					case "test":
					default:
						usercol.EvaluationType = UserEvaluationType.Test;
						break;
				}

				if (syncLimitToZone.Trim().Length > 0) {
					usercol.ZoneID = syncLimitToZone.Trim();
					usercol.EvaluationType |= UserEvaluationType.LimitToZone;
				}

				if (syncIgnorePidRegEx.Trim().Length > 0) {
					usercol.IgnoreIDRegex = syncIgnorePidRegEx.Trim();
				}

				if (syncIgnorePid.Count > 0) {
					usercol.IgnoreIDList = syncIgnorePid.ToArray();
				}

				if (syncIgnoreCase == true) {
					usercol.EvaluationType |= UserEvaluationType.IgnoreCaseInID;
				}

				if (syncAllowPidNull == true) {
					usercol.EvaluationType |= UserEvaluationType.AllowPidNull;
				}

				usercol.MaxSynchronizationLevel = (syncMaximumLevel);

				// PID = MA# for SOFD users, and SamAccountName for AD users.
				// Add users.
				List<String> userPids = new List<String>();
				usercol.Users = new UserDataCollection();

				// Add users who is member of the groupCareAccessUser group.
				foreach (AdUser user in this.GetAllUsers(groupCareAccessUser)) {
					if ((userPids.Contains(user.SamAccountName) == false) &&
						((syncDeleteDisabledUsers == false) || (user.Enabled == true))) {
						UserData user1 = new UserData();
						user1.Pid = "AD-" + user.SamAccountName;
						user1.Name = user.DisplayName;
						user1.Phone = user.TelephoneNumber;
						if ((user1.Pid != null) && (user1.Pid.Trim().Length > 0)) {
							usercol.Users.Add(user1);
						}

						// Log.
						this.LogDebug("Found user in Active Directory ({0} - {1})", user1.Pid, user1.Name);
					}
					userPids.Add(user.SamAccountName);
				}

				// Add users from SOFD that matches the job title id.
				foreach (String sofdJobTitleId in sofdJobTitleIds) {
					try {
						List<SofdEmployee> employees = this.GetAllEmployees(
							new SofdEmployeeFilter_Aktiv(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, true),
							new SofdEmployeeFilter_StillingsId(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, Int32.Parse(sofdJobTitleId))
						);
						foreach (SofdEmployee employee in employees) {
							if (userPids.Contains(employee.MaNummer.ToString()) == false) {
								UserData user1 = new UserData();
								user1.Pid = "MA-" + employee.MaNummer.ToString();
								user1.Name = employee.Navn;
								user1.Phone = employee.TelefonNummer;
								if ((user1.Pid != null) && (user1.Pid.Trim().Length > 0)) {
									usercol.Users.Add(user1);
								}
								userPids.Add(employee.MaNummer.ToString());

								// Log.
								this.LogDebug("Found user in SOFD by job title id ({0} - {1})", user1.Pid, user1.Name);
							}
						}
					} catch { }
				}

				// Add users from SOFD that matches the job title name.
				foreach (String sofdJobTitleName in sofdJobTitleNames) {
					try {
						List<SofdEmployee> employees = this.GetAllEmployees(
							new SofdEmployeeFilter_Aktiv(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, true),
							new SofdEmployeeFilter_StillingsBetegnelse(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, sofdJobTitleName)
						);
						foreach (SofdEmployee employee in employees) {
							if (userPids.Contains(employee.MaNummer.ToString()) == false) {
								UserData user1 = new UserData();
								user1.Pid = "MA-" + employee.MaNummer.ToString();
								user1.Name = employee.Navn;
								user1.Phone = employee.TelefonNummer;
								if ((user1.Pid != null) && (user1.Pid.Trim().Length > 0)) {
									usercol.Users.Add(user1);
								}
								userPids.Add(employee.MaNummer.ToString());

								// Log.
								this.LogDebug("Found user in SOFD by job title ({0} - {1})", user1.Pid, user1.Name);
							}
						}
					} catch { }
				}

				// Add users from SOFD that matches the organization id.
				foreach (String sofdOrganizationId in sofdOrganizationIds) {
					try {
						List<SofdEmployee> employees = this.GetAllEmployees(
							new SofdEmployeeFilter_Aktiv(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, true),
							new SofdEmployeeFilter_OrganisationId(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, Int32.Parse(sofdOrganizationId))
						);
						foreach (SofdEmployee employee in employees) {
							if (userPids.Contains(employee.MaNummer.ToString()) == false) {
								UserData user1 = new UserData();
								user1.Pid = "MA-" + employee.MaNummer.ToString();
								user1.Name = employee.Navn;
								user1.Phone = employee.TelefonNummer;
								if ((user1.Pid != null) && (user1.Pid.Trim().Length > 0)) {
									usercol.Users.Add(user1);
								}
								userPids.Add(employee.MaNummer.ToString());

								// Log.
								this.LogDebug("Found user in SOFD by organization id ({0} - {1})", user1.Pid, user1.Name);
							}
						}
					} catch { }
				}

				// Add users from SOFD that matches the organization name.
				foreach (String sofdOrganizationName in sofdOrganizationNames) {
					try {
						List<SofdEmployee> employees = this.GetAllEmployees(
							new SofdEmployeeFilter_Aktiv(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, true),
							new SofdEmployeeFilter_OrganisationNavn(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, sofdOrganizationName)
						);
						foreach (SofdEmployee employee in employees) {
							if (userPids.Contains(employee.MaNummer.ToString()) == false) {
								UserData user1 = new UserData();
								user1.Pid = "MA-" + employee.MaNummer.ToString();
								user1.Name = employee.Navn;
								user1.Phone = employee.TelefonNummer;
								if ((user1.Pid != null) && (user1.Pid.Trim().Length > 0)) {
									usercol.Users.Add(user1);
								}
								userPids.Add(employee.MaNummer.ToString());

								// Log.
								this.LogDebug("Found user in SOFD by organization ({0} - {1})", user1.Pid, user1.Name);
							}
						}
					} catch { }
				}

				// Add users from SOFD that matches the pay class.
				foreach (String sofdPayClassName in sofdPayClassNames) {
					try {
						List<SofdEmployee> employees = this.GetAllEmployees(
							new SofdEmployeeFilter_Aktiv(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, true),
							new SofdEmployeeFilter_LoenKlasse(SqlWhereFilterOperator.AND, SqlWhereFilterValueOperator.Equals, sofdPayClassName)
						);
						foreach (SofdEmployee employee in employees) {
							if (userPids.Contains(employee.MaNummer.ToString()) == false) {
								UserData user1 = new UserData();
								user1.Pid = "MA-" + employee.MaNummer.ToString();
								user1.Name = employee.Navn;
								user1.Phone = employee.TelefonNummer;
								if ((user1.Pid != null) && (user1.Pid.Trim().Length > 0)) {
									usercol.Users.Add(user1);
								}
								userPids.Add(employee.MaNummer.ToString());

								// Log.
								this.LogDebug("Found user in SOFD by pay class ({0} - {1})", user1.Pid, user1.Name);
							}
						}
					} catch { }
				}

				// Fail on no users.
				if ((failOnNoUsers == true) && (usercol.Users.Count == 0)) {
					throw new Exception("No users found.");
				}

				// Log.
				this.Log("Synchronizing {0} users ({1}).", usercol.Users.Count, syncEvaluationType);

				// Create the REST client.
				RestClient restClientSynchronize = new RestClient(hostUrlSynchronize.Scheme + "://" + hostUrlSynchronize.Host + (hostUrlSynchronize.IsDefaultPort ? "" : ":" + hostUrlSynchronize.Port.ToString(CultureInfo.InvariantCulture)));
				restClientSynchronize.Authenticator = new HttpBasicAuthenticator(userName, userPassword);

				// Create REST request.
				RestRequest restRequestSynchronize = new RestRequest(hostUrlSynchronize.PathAndQuery, syncRestMethod);
				restRequestSynchronize.XmlSerializer = new XmlDataContractSerializer();
				restRequestSynchronize.RequestFormat = DataFormat.Xml;

				restClientSynchronize.AddHandler("text/xml", new XmlDataContractSerializer());
				restClientSynchronize.AddHandler("application/xml", new XmlDataContractSerializer());

				// Send the request.
				restRequestSynchronize.AddBody(usercol);
				IRestResponse<EvaluateUserCollectionResult> restResponseSynchronize = restClientSynchronize.Execute<EvaluateUserCollectionResult>(restRequestSynchronize);

				// Log result.
				if (restResponseSynchronize.StatusCode == HttpStatusCode.OK) {
					this.Log("The following users was synchronized.");
					if (restResponseSynchronize.Data.AddedUsers != null) {
						foreach (UserData user in restResponseSynchronize.Data.AddedUsers) {
							this.Log("Added: {0}, {1}", user.Pid, user.Name);
						}
					}
					if (restResponseSynchronize.Data.UpdatedUsers != null) {
						foreach (UserData user in restResponseSynchronize.Data.UpdatedUsers) {
							this.Log("Updated: {0}, {1}", user.Pid, user.Name);
						}
					}
					if (restResponseSynchronize.Data.DeletedUsers != null) {
						foreach (UserData user in restResponseSynchronize.Data.DeletedUsers) {
							this.Log("Deleted: {0}, {1}", user.Pid, user.Name);
						}
					}
					if (restResponseSynchronize.Data.IgnoredUsers != null) {
						foreach (UserData user in restResponseSynchronize.Data.IgnoredUsers) {
							this.Log("Ignored: {0}, {1}", user.Pid, user.Name);
						}
					}
					if ((restResponseSynchronize.Data.AddedUsers == null) && (restResponseSynchronize.Data.UpdatedUsers == null) && (restResponseSynchronize.Data.DeletedUsers == null) && (restResponseSynchronize.Data.IgnoredUsers == null)) {
						this.Log("No change.");
					}
				} else {
					this.LogError("Synchronizing error: {0} - {1}. {2}", restResponseSynchronize.StatusCode, restResponseSynchronize.StatusDescription, restResponseSynchronize.ErrorMessage);
				}



				//-----------------------------------------------------------------------------------------------------------------------------------
				// Report.
				//-----------------------------------------------------------------------------------------------------------------------------------
				// Build HTML report.
				HtmlBuilder html = new HtmlBuilder();
				List<List<String>> table = new List<List<String>>();

				// Add message.
				if (syncEvaluationType.Trim().ToLower() == "test") {
					//(usercol.EvaluationType.HasFlag(UserEvaluationType.Test) == true) {
					html.AppendParagraph(
						"This automatic task is DEACTIVATED.",
						"When it is enabled, it will synchronize users between the active directory and Access Technology REST service."
					);
				} else {
					html.AppendParagraph("This automatic task has synchronized users between the active directory and Access Technology REST service.");
				}

				if (restResponseSynchronize.StatusCode == HttpStatusCode.OK) {
					// Add added users.
					if (restResponseSynchronize.Data.AddedUsers != null) {
						table.Clear();
						table.Add(new List<String>() { "Userid", "Full name", "Phone", "Card" });
						foreach (UserData user in restResponseSynchronize.Data.AddedUsers) {
							table.Add(new List<String>() { user.Pid, user.Name, user.Phone, user.Card });
						}
						table.Add(new List<String>() { restResponseSynchronize.Data.AddedUsers.Count + " added users" });

						html.AppendHeading2("Added users");
						html.AppendHorizontalTable(table, 1, 1);
					}

					// Add updated users.
					if (restResponseSynchronize.Data.UpdatedUsers != null) {
						table.Clear();
						table.Add(new List<String>() { "Userid", "Full name", "Phone", "Card" });
						foreach (UserData user in restResponseSynchronize.Data.UpdatedUsers) {
							table.Add(new List<String>() { user.Pid, user.Name, user.Phone, user.Card });
						}
						table.Add(new List<String>() { restResponseSynchronize.Data.UpdatedUsers.Count + " updated users" });

						html.AppendHeading2("Updated users");
						html.AppendHorizontalTable(table, 1, 1);
					}

					// Add deleted users.
					if (restResponseSynchronize.Data.DeletedUsers != null) {
						table.Clear();
						table.Add(new List<String>() { "Userid", "Full name", "Phone", "Card" });
						foreach (UserData user in restResponseSynchronize.Data.DeletedUsers) {
							table.Add(new List<String>() { user.Pid, user.Name, user.Phone, user.Card });
						}
						table.Add(new List<String>() { restResponseSynchronize.Data.DeletedUsers.Count + " deleted users" });

						html.AppendHeading2("Deleted users");
						html.AppendHorizontalTable(table, 1, 1);
					}

					// Add ignored users.
					if (restResponseSynchronize.Data.IgnoredUsers != null) {
						table.Clear();
						table.Add(new List<String>() { "Userid", "Full name", "Phone", "Card" });
						foreach (UserData user in restResponseSynchronize.Data.IgnoredUsers) {
							table.Add(new List<String>() { user.Pid, user.Name, user.Phone, user.Card });
						}
						table.Add(new List<String>() { restResponseSynchronize.Data.IgnoredUsers.Count + " ignored users" });

						html.AppendHeading2("Ignored users");
						html.AppendHorizontalTable(table, 1, 1);
					}
					if ((restResponseSynchronize.Data.AddedUsers == null) && (restResponseSynchronize.Data.UpdatedUsers == null) && (restResponseSynchronize.Data.DeletedUsers == null) && (restResponseSynchronize.Data.IgnoredUsers == null)) {
						html.AppendHeading2("Ignored users");
						html.AppendParagraph("No users were added, updated or deleted.");
					}
				} else {
					// Communication error.
					table.Clear();
					table.Add(new List<String>() { "Status code", restResponseSynchronize.StatusCode.ToString()});
					table.Add(new List<String>() { "Status text", restResponseSynchronize.StatusDescription});
					table.Add(new List<String>() { "URI", restResponseSynchronize.ResponseUri.ToString()});
					foreach (Parameter header in restResponseSynchronize.Headers) {
						table.Add(new List<String>() { "Header: " + header.Name, header.Value.ToString() });
					}
					table.Add(new List<String>() { "Content", restResponseSynchronize.Content });
					table.Add(new List<String>() { "Error", restResponseSynchronize.ErrorMessage});

					html.AppendHeading2("Communication error");
					html.AppendVerticalTable(table);
				}

				// Queried users, and updated MiFare identifiers.
				if ((queryUserUpdatedAD.Count > 0) || (queryUserUpdatedSOFD.Count > 0)) {
					table.Clear();
					table.Add(new List<String>() { "", "ID", "Full name", "Card/MiFare" });
					foreach (AdUser adUser in queryUserUpdatedAD) {
						table.Add(new List<String>() { "AD", adUser.SamAccountName, adUser.Name, this.GetUserMiFareId(adUser) });
					}
					foreach (SofdEmployee employee in queryUserUpdatedSOFD) {
						table.Add(new List<String>() { "SOFD", employee.MaNummer.ToString(), employee.Navn, employee.MiFareId });
					}
					table.Add(new List<String>() { queryUserUpdatedAD.Count + " AD users, " + queryUserUpdatedSOFD.Count + " SOFD users" });

					html.AppendHeading2("Updated MiFare identifiers");
					html.AppendHorizontalTable(table, 1, 1);
				}

				// Configuration.
				table.Clear();
				table.Add(new List<String>() { "Group user", groupNameCareAccessUser });
				if (failOnNoUsers == true) {
					table.Add(new List<String>() { "Fail", "Fail when the 'GroupUser' does not contain any users." });
				} else {
					table.Add(new List<String>() { "Fail", "Continue when the 'GroupUser' does not contain any users. Warning: This might delete users." });
				}

				table.Add(new List<String>() { "Host URL", hostUrlSynchronize.ToString() });
				table.Add(new List<String>() { "User name", userName });
				
				if ((usercol.EvaluationType & UserEvaluationType.AddNewUsers) == UserEvaluationType.AddNewUsers) {
					table.Add(new List<String>() { "Flag", "Adding new users." });
				} else {
					table.Add(new List<String>() { "Flag", "Ignoring and not adding new users." });
				}
				if ((usercol.EvaluationType & UserEvaluationType.RemoveNotPresent) == UserEvaluationType.RemoveNotPresent) {
					table.Add(new List<String>() { "Flag", "Deleting users that are not included in the synchronizing." });
				} else {
					table.Add(new List<String>() { "Flag", "Ignoring and not deleting users that are not included in the synchronizing." });
				}

				// Flag: AllowPidNull.
				if ((usercol.EvaluationType & UserEvaluationType.AllowPidNull) == UserEvaluationType.AllowPidNull) {
					table.Add(new List<String>() { "Flag", "Allow empty user Pid." });
				} else {
					table.Add(new List<String>() { "Flag", "Do not allow empty user Pid." });
				}

				if (queryUserMiFareIdAD == true) {
					table.Add(new List<String>() { "Flag", "Query MiFare Ids and save in Active Directory." });
				} else {
					table.Add(new List<String>() { "Flag", "Do not query MiFare Ids and save in Active Directory." });
				}
				if (queryUserMiFareIdSOFD == true) {
					table.Add(new List<String>() { "Flag", "Query MiFare Ids and save in SOFD Directory." });
				} else {
					table.Add(new List<String>() { "Flag", "Do not query MiFare Ids and save in SOFD Directory." });
				}

				table.Add(new List<String>() { "Maximum user updates", syncMaximumLevel.ToString() });
				table.Add(new List<String>() { "All found users", usercol.Users.Count.ToString() });

				html.AppendHeading2("Configuration");
				html.AppendVerticalTable(table);

				// Send message.
				if (configMessageSend == true) {
					if (configMessageTo.Count > 0) {
						this.SendMail(
							String.Join(";", configMessageTo.ToArray()),
							configMessageSubject,
							html.ToString(),
							true
						);
					} else {
						this.SendMail(
							configMessageSubject,
							html.ToString(),
							true
						);
					}
				}
			} catch (Exception exception) {
				// Send message on error.
				this.SendMail("Error " + this.GetName(), exception.Message, false);

				// Throw the error.
				throw;
			}
		} // Run
		#endregion

	} // AcctPlugin
	#endregion

} // NDK.AcctPlugin